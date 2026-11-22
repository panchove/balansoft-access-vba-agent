// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 26514:2022
// Architecture Reference: ADR-003-ABORT-MODEL.md / ADR-007-NIM-CLIENT.md / ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Cliente HTTP SSE para la API Nvidia NIM con tool calls, cancelación cooperativa según ADR-003 y ADR-007,
/// retry con exponential backoff + jitter y circuit breaker para 429/5xx, y timeout por chunk.
/// Cumple con ISO/IEC 25010 (Resiliencia de Red) y el Pilar 22 (IEEE 7009-2024).
/// </summary>
public sealed class BlsNimClient : IBlsNimClient
{
    private const string DefaultEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
    private const int ChunkTimeoutSeconds = 120;
    private const int MaxRetries = 3;
    private const int MaxRetryAfterSeconds = 30;

    private static readonly TimeSpan[] BackoffDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

    private readonly HttpClient _httpClient;
    private readonly IBlsSecureStore _secureStore;
    private readonly IBlsLogger _logger;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly TimeSpan _chunkTimeout;
    private readonly IBlsNimRetryPolicy _retryPolicy;
    private readonly IBlsCircuitBreaker _breaker;

    /// <summary>Constructor público: política de producción con jitter y breaker reales.</summary>
    public BlsNimClient(
        HttpClient httpClient,
        IBlsSecureStore secureStore,
        IBlsLogger logger,
        string model = null,
        string endpoint = null)
        : this(
            httpClient,
            secureStore,
            logger,
            model,
            endpoint,
            null,
            new BlsExponentialJitterRetryPolicy(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(MaxRetryAfterSeconds)),
            new BlsCircuitBreaker())
    {
    }

    /// <summary>
    /// Constructor heredado usado por los tests del comportamiento anterior:
    /// backoff fijo y breaker inerte, para que los delays sigan siendo exactos.
    /// </summary>
    internal BlsNimClient(
        HttpClient httpClient,
        IBlsSecureStore secureStore,
        IBlsLogger logger,
        string model,
        string endpoint,
        TimeSpan? chunkTimeout,
        TimeSpan[] backoffDelays)
        : this(
            httpClient,
            secureStore,
            logger,
            model,
            endpoint,
            chunkTimeout,
            new BlsFixedRetryPolicy(backoffDelays ?? BackoffDelays),
            BlsNoOpCircuitBreaker.Instance)
    {
    }

    /// <summary>Constructor F4: inyecta política, breaker y reloj para tests deterministas.</summary>
    internal BlsNimClient(
        HttpClient httpClient,
        IBlsSecureStore secureStore,
        IBlsLogger logger,
        string model,
        string endpoint,
        TimeSpan? chunkTimeout,
        IBlsNimRetryPolicy retryPolicy,
        IBlsCircuitBreaker breaker)
    {
        _httpClient = BlsContract.RequiresNotNull(httpClient, nameof(httpClient));
        _secureStore = BlsContract.RequiresNotNull(secureStore, nameof(secureStore));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
        _retryPolicy = BlsContract.RequiresNotNull(retryPolicy, nameof(retryPolicy));
        _breaker = BlsContract.RequiresNotNull(breaker, nameof(breaker));
        _model = model;
        _endpoint = string.IsNullOrEmpty(endpoint) ? DefaultEndpoint : endpoint;
        _chunkTimeout = chunkTimeout ?? TimeSpan.FromSeconds(ChunkTimeoutSeconds);

        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsNimClient", "No se pudo fijar TLS 1.2/1.3; se usa la configuracion del host", ex);
        }
    }

    public async Task SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IBlsNimSink sink,
        CancellationToken ct)
    {
        BlsContract.RequiresNotNull(sink, nameof(sink));
        BlsContract.RequiresNotNull(messages, nameof(messages));
        BlsContract.RequiresNotNull(tools, nameof(tools));

        try
        {
            if (!_secureStore.TryGetApiKey(out var apiKey) || string.IsNullOrEmpty(apiKey))
            {
                sink.OnError(new BlsNimAuthException("API key no configurada"));
                return;
            }

            var json = NimHttpRequestBuilder.BuildJson(
                NimHttpRequestBuilder.ResolveModel(_model),
                messages,
                tools);

            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                // ADR-003: la cancelación del turno se comprueba antes que
                // cualquier decisión del breaker y antes de cada espera.
                ct.ThrowIfCancellationRequested();

                // En HalfOpen solo se permite un sondeo: si falla, se abre
                // de inmediato (R012) en lugar de consumir los 3 reintentos.
                var isProbe = _breaker.State == BlsCircuitState.HalfOpen;
                if (!_breaker.AllowCall())
                {
                    sink.OnError(new BlsNimCircuitOpenException(
                        "Circuit breaker abierto: NIM se considera temporalmente indisponible"));
                    return;
                }

                using (var request = CreateRequest(json, apiKey))
                using (var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        await ProcessStreamAsync(response, sink, ct).ConfigureAwait(false);
                        _breaker.RecordSuccess();
                        return;
                    }

                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    var isLastAttempt = attempt == MaxRetries - 1;
                    var retryDelay = ResolveRetry(response, attempt);

                    if (!retryDelay.HasValue || isLastAttempt || isProbe)
                    {
                        // 429/5xx agotados indican indisponibilidad; los 4xx
                        // distintos de 429 son errores del cliente y no cuentan.
                        if (retryDelay.HasValue)
                        {
                            RecordBreakerFailure();
                        }

                        ReportError(response.StatusCode, body, sink);
                        return;
                    }

                    await Task.Delay(retryDelay.Value, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sink.OnCancelled();
        }
        catch (BlsNimTimeoutException ex)
        {
            RecordBreakerFailure();
            sink.OnError(ex);
        }
        catch (BlsNimException ex)
        {
            sink.OnError(ex);
        }
        catch (Exception ex)
        {
            RecordBreakerFailure();
            _logger.Error("BlsNimClient", "Excepción no controlada en SendAsync", ex);
            var inner = ex.InnerException != null ? $": {ex.InnerException.Message}" : "";
            sink.OnError(new BlsNimException($"Error de conexión con NIM ({ex.Message}{inner})", null, ex));
        }
    }

    private void RecordBreakerFailure()
    {
        var wasClosed = _breaker.State == BlsCircuitState.Closed;
        _breaker.RecordFailure();

        if (wasClosed && _breaker.State == BlsCircuitState.Open)
        {
            _logger.Warn("BlsNimClient",
                $"Circuit breaker abierto tras {MaxRetries} intentos fallidos: se suspenden nuevas llamadas a NIM");
        }
    }

    private HttpRequestMessage CreateRequest(string json, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private async Task ProcessStreamAsync(HttpResponseMessage response, IBlsNimSink sink, CancellationToken ct)
    {
        var parser = new NimStreamParser(sink);

        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var reader = new SseLineReader(stream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using (var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                chunkCts.CancelAfter(_chunkTimeout);

                string line;
                try
                {
                    line = await reader.ReadLineAsync(chunkCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new BlsNimTimeoutException(
                        $"Timeout de {(int)_chunkTimeout.TotalSeconds}s sin recibir datos de NIM");
                }

                if (line == null)
                {
                    // EOF sin [DONE]: se emiten las tool calls pendientes.
                    parser.Complete();
                    sink.OnDone();
                    return;
                }

                if (line.Length == 0) continue;
                if (line[0] == ':') continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line.Substring(5).TrimStart();
                if (data.Length == 0) continue;

                if (data == "[DONE]")
                {
                    parser.Complete();
                    sink.OnDone();
                    return;
                }

                parser.Feed(data);
            }
        }
    }

    /// <summary>
    /// Devuelve el delay a esperar antes de reintentar, o null si el status
    /// no es reintentable. Retry-After prevalece sobre el jitter y se acota
    /// a 30 s (doc §9.8); en su ausencia se usa la política inyectada.
    /// </summary>
    private TimeSpan? ResolveRetry(HttpResponseMessage response, int attempt)
    {
        var status = (int)response.StatusCode;

        if (status == 429 || status >= 500)
        {
            var retryAfter = ParseRetryAfter(response);
            if (retryAfter.HasValue) return retryAfter;

            return _retryPolicy.NextDelay(attempt);
        }

        return null;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers?.RetryAfter;
        if (retryAfter == null) return null;

        if (retryAfter.Delta.HasValue)
        {
            var seconds = Math.Min(retryAfter.Delta.Value.TotalSeconds, MaxRetryAfterSeconds);
            return TimeSpan.FromSeconds(Math.Max(seconds, 0));
        }

        if (retryAfter.Date.HasValue)
        {
            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            if (delta <= TimeSpan.Zero) return TimeSpan.Zero;

            return delta > TimeSpan.FromSeconds(MaxRetryAfterSeconds)
                ? TimeSpan.FromSeconds(MaxRetryAfterSeconds)
                : delta;
        }

        return null;
    }

    private static void ReportError(HttpStatusCode status, string body, IBlsNimSink sink)
    {
        var code = (int)status;

        if (code == 401)
        {
            sink.OnError(new BlsNimAuthException("API key inválida", body));
            return;
        }

        if (code == 429)
        {
            sink.OnError(new BlsNimRateLimitException(
                $"Rate limit agotado tras {MaxRetries} intentos: {body}", body));
            return;
        }

        if (code >= 500)
        {
            sink.OnError(new BlsNimServerException(
                $"Server error {code} tras {MaxRetries} intentos: {body}", body));
            return;
        }

        sink.OnError(new BlsNimException($"HTTP {code}: {body}", body));
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            if (response.Content == null) return string.Empty;
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        // HttpClient es inyectado: no lo poseemos.
    }
}
