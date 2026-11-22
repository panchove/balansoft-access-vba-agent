// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md / specs/018-IDEMPOTENCY-RESILIENCE.md

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.Tests.Infrastructure;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

/// <summary>
/// F4 / R-09 — Backoff con full jitter y circuit breaker del cliente NIM.
/// No hay esperas fijas: el reloj del breaker y la función de
/// aleatoriedad se inyectan (skill 09).
/// </summary>
[TestClass]
public class BlsNimResilienceTests
{
    private FakeHttpHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private FakeSecureStore _secureStore = null!;
    private FakeLogger _logger = null!;
    private RecordingSink _sink = null!;
    private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Setup()
    {
        _handler = new FakeHttpHandler();
        _httpClient = new HttpClient(_handler);
        _secureStore = new FakeSecureStore();
        _secureStore.SaveApiKey("test-key");
        _logger = new FakeLogger();
        _sink = new RecordingSink();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _httpClient?.Dispose();
    }

    /// <summary>Política espía: cuenta cuándo NIM intenta aplicar jitter.</summary>
    private sealed class SpyRetryPolicy : IBlsNimRetryPolicy
    {
        public int Calls { get; private set; }
        private readonly TimeSpan _delay;

        public SpyRetryPolicy(TimeSpan? delay = null) => _delay = delay ?? TimeSpan.Zero;

        public TimeSpan NextDelay(int attempt)
        {
            Calls++;
            return _delay;
        }
    }

    private BlsNimClient CreateClient(
        IBlsNimRetryPolicy? policy = null,
        IBlsCircuitBreaker? breaker = null)
    {
        return new BlsNimClient(
            _httpClient,
            _secureStore,
            _logger,
            null,
            null,
            TimeSpan.FromMilliseconds(150),
            policy ?? new SpyRetryPolicy(),
            breaker ?? BlsNoOpCircuitBreaker.Instance);
    }

    private Task SendAsync(BlsNimClient client, CancellationToken ct)
    {
        return client.SendAsync(
            new List<ChatMessage> { new ChatMessage("user", "Hola") },
            new List<ToolDefinition>(),
            _sink,
            ct);
    }

    private BlsCircuitBreaker CreateBreaker(int threshold, TimeSpan? openDuration = null)
    {
        return new BlsCircuitBreaker(threshold, openDuration ?? TimeSpan.FromSeconds(30), () => _now);
    }

    // ---------- Jitter ----------

    [TestMethod]
    public void Retry_JitterDistribuyeRetrasos()
    {
        var factors = new Queue<double>(new[] { 0.0, 0.5, 1.0 });
        var policy = new BlsExponentialJitterRetryPolicy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            () => factors.Dequeue());

        var delays = new[] { policy.NextDelay(0), policy.NextDelay(0), policy.NextDelay(0) };

        Assert.AreEqual(0d, delays[0].TotalMilliseconds);
        Assert.AreEqual(500d, delays[1].TotalMilliseconds);
        Assert.AreEqual(1000d, delays[2].TotalMilliseconds);
    }

    [TestMethod]
    public void Retry_ExponencialConJitter_RespetaTope()
    {
        var policy = new BlsExponentialJitterRetryPolicy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            () => 1.0);

        Assert.AreEqual(TimeSpan.FromSeconds(1), policy.NextDelay(0));
        Assert.AreEqual(TimeSpan.FromSeconds(2), policy.NextDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(4), policy.NextDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.NextDelay(20),
            "El tope de 30 s prevalece sobre el crecimiento exponencial.");
    }

    [TestMethod]
    public async Task RetryAfter_PrevaleceSobreJitter()
    {
        var policy = new SpyRetryPolicy();
        var client = CreateClient(policy: policy);
        _handler.EnqueueError((HttpStatusCode)429, "rate limited", TimeSpan.Zero);
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(client, CancellationToken.None).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, policy.Calls,
            "Retry-After es una instrucción del servidor: no debe añadirse jitter.");
        Assert.AreEqual(2, _handler.RequestCount);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    // ---------- Circuit breaker ----------

    [TestMethod]
    public async Task Breaker_Abierto_NoHaceRequestYEmiteCircuitOpen()
    {
        var breaker = CreateBreaker(5);
        for (var i = 0; i < 5; i++) breaker.RecordFailure();
        Assert.AreEqual(BlsCircuitState.Open, breaker.State);

        var client = CreateClient(breaker: breaker);
        await SendAsync(client, CancellationToken.None).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, _handler.RequestCount,
            "Con el breaker abierto no se llama a HttpClient.");
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimCircuitOpenException));
        Assert.AreEqual(0, _sink.DoneCount);
        Assert.AreEqual(0, _sink.CancelledCount);
    }

    [TestMethod]
    public async Task Breaker_TrasOpenDuration_PasaAHalfOpenYSeCierraConExito()
    {
        var breaker = CreateBreaker(1);
        breaker.RecordFailure();
        Assert.AreEqual(BlsCircuitState.Open, breaker.State);

        _now = _now.AddSeconds(31);
        Assert.AreEqual(BlsCircuitState.HalfOpen, breaker.State,
            "La property State debe reflejar HalfOpen sin consumir el sondeo.");

        _handler.EnqueueSseSuccess("[DONE]");
        var client = CreateClient(breaker: breaker);
        await SendAsync(client, CancellationToken.None).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.AreEqual(BlsCircuitState.Closed, breaker.State,
            "Un éxito en HalfOpen cierra el circuito.");
    }

    [TestMethod]
    public async Task Breaker_HalfOpenConFallo_VuelveAOpen()
    {
        var breaker = CreateBreaker(1);
        breaker.RecordFailure();
        _now = _now.AddSeconds(31);
        Assert.AreEqual(BlsCircuitState.HalfOpen, breaker.State);

        for (var i = 0; i < 3; i++)
            _handler.EnqueueError((HttpStatusCode)429, "rate limited");

        var client = CreateClient(policy: new SpyRetryPolicy(), breaker: breaker);
        await SendAsync(client, CancellationToken.None).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount,
            "En HalfOpen solo se permite un sondeo, no los 3 reintentos.");
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.AreEqual(BlsCircuitState.Open, breaker.State,
            "Un fallo en HalfOpen vuelve a abrir el circuito y reinicia la ventana.");
    }

    [TestMethod]
    public async Task Breaker_Exito_ReseteaContador()
    {
        var breaker = CreateBreaker(3);
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.AreEqual(BlsCircuitState.Closed, breaker.State);

        _handler.EnqueueSseSuccess("[DONE]");
        await SendAsync(CreateClient(breaker: breaker), CancellationToken.None)
            .WithTimeout(TaskTestExtensions.DefaultTimeout);

        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.AreEqual(BlsCircuitState.Closed, breaker.State,
            "Tras un éxito los fallos anteriores no cuentan: hace falta otro ciclo completo.");

        _handler.EnqueueSseSuccess("[DONE]");
        await SendAsync(CreateClient(breaker: breaker), CancellationToken.None)
            .WithTimeout(TaskTestExtensions.DefaultTimeout);
        Assert.AreEqual(2, _handler.RequestCount);
    }

    [TestMethod]
    public async Task Breaker_Error4xx_NoCuentaComoFallo()
    {
        var breaker = CreateBreaker(1);
        _handler.EnqueueError(HttpStatusCode.BadRequest, "campo inválido");

        await SendAsync(CreateClient(breaker: breaker), CancellationToken.None)
            .WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsFalse(_sink.Errors[0] is BlsNimCircuitOpenException);
        Assert.AreEqual(BlsCircuitState.Closed, breaker.State,
            "Un 4xx es un error del cliente, no indisponibilidad del proveedor.");
    }

    [TestMethod]
    public async Task Cancelacion_ConBreakerAbierto_EmiteCancelledNoError()
    {
        var breaker = CreateBreaker(1);
        breaker.RecordFailure();
        Assert.AreEqual(BlsCircuitState.Open, breaker.State);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await SendAsync(CreateClient(breaker: breaker), cts.Token)
            .WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, _handler.RequestCount);
        Assert.AreEqual(1, _sink.CancelledCount);
        Assert.AreEqual(0, _sink.Errors.Count,
            "ADR-003: la cancelación gana sobre el circuito abierto.");
    }

    /// <summary>Sink que registra eventos para verificar el contrato del cliente.</summary>
    private sealed class RecordingSink : IBlsNimSink
    {
        public List<string> TextChunks { get; } = new List<string>();
        public List<ToolCallComplete> ToolCalls { get; } = new List<ToolCallComplete>();
        public List<BlsNimException> Errors { get; } = new List<BlsNimException>();
        public int DoneCount { get; private set; }
        public int CancelledCount { get; private set; }

        public void OnTextChunk(string text) => TextChunks.Add(text);
        public void OnToolCall(ToolCallComplete call) => ToolCalls.Add(call);
        public void OnError(BlsNimException ex) => Errors.Add(ex);
        public void OnDone() => DoneCount++;
        public void OnCancelled() => CancelledCount++;
    }
}
