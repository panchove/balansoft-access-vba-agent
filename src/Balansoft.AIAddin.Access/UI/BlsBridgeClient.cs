// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Core.Protocol;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Cliente JSON-RPC 2.0 sobre Named Pipe para comunicación con BlsVbeBridgeServer.
/// </summary>
public sealed class BlsBridgeClient : IBlsBridgeClient
{
    private const int DefaultToolTimeoutSeconds = 30;
    private const int WriteTimeoutSeconds = 10;

    private readonly string _pipeName;
    private readonly IBlsLogger _logger;
    private readonly TimeSpan _toolTimeout;

    // El protocolo es estrictamente request/response sobre un único pipe: se
    // serializan las invocaciones para que dos llamadas concurrentes no
    // intercalen sus frames ni roben la respuesta de la otra.
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private NamedPipeClientStream? _pipe;
    private long _requestId;
    private bool _disposed;

    public BlsBridgeClient(
        string pipeName = BlsPipeFraming.DefaultPipeName,
        IBlsLogger logger = null!,
        TimeSpan? toolTimeout = null)
    {
        _pipeName = BlsPipeFraming.NormalizePipeName(pipeName);
        _logger = logger;
        _toolTimeout = toolTimeout ?? TimeSpan.FromSeconds(DefaultToolTimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<TResult> InvokeAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken ct)
    {
        BlsContract.RequiresState(!_disposed, "El cliente del bridge ha sido dispuesto.");
        BlsContract.RequiresNotNullOrWhiteSpace(method, nameof(method));

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BlsBridgeClient));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pipe == null || !_pipe.IsConnected)
            {
                await ConnectAsync(ct).ConfigureAwait(false);
            }

            var request = new JsonRpcRequest(
                "2.0",
                JsonSerializer.SerializeToElement(Interlocked.Increment(ref _requestId)),
                method,
                parameters != null ? JsonSerializer.SerializeToElement(parameters) : (JsonElement?)null);

            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
            var header = BitConverter.GetBytes((uint)requestBytes.Length);

            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            writeCts.CancelAfter(TimeSpan.FromSeconds(WriteTimeoutSeconds));

            var stream = _pipe;
            if (stream == null)
            {
                throw new BlsBridgeException("Pipe no conectado");
            }

            await stream.WriteAsync(header, 0, 4, writeCts.Token).ConfigureAwait(false);
            await stream.WriteAsync(requestBytes, 0, requestBytes.Length, writeCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(writeCts.Token).ConfigureAwait(false);

            var response = await ReadResponseAsync(ct).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw MapError(response.Error);
            }

            if (response.Result == null)
            {
                return default!;
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            return JsonSerializer.Deserialize<TResult>(resultJson)!;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // El turno se canceló: la conexión queda en estado desconocido.
            Disconnect();
            throw;
        }
        catch (IOException)
        {
            // Pipe roto: se descarta para forzar reconexión en la próxima llamada.
            Disconnect();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        Disconnect();

        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)Math.Min(int.MaxValue, TimeSpan.FromSeconds(10).TotalMilliseconds), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        _pipe = pipe;

        _logger.Info("BlsBridgeClient", "Conectado al bridge");
    }

    private void Disconnect()
    {
        try { _pipe?.Dispose(); } catch { throw; }
        _pipe = null;
    }

    private async Task<JsonRpcResponse> ReadResponseAsync(CancellationToken ct)
    {
        var pipe = _pipe;
        if (pipe == null)
        {
            throw new BlsBridgeException("Pipe no conectado");
        }

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(_toolTimeout);

        byte[] payload;
        try
        {
            payload = await BlsPipeFraming.ReadFrameAsync(pipe, readCts.Token).ConfigureAwait(false)
                ?? throw new IOException("Conexión cerrada por el servidor");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BlsBridgeException($"Timeout de {(int)_toolTimeout.TotalSeconds}s esperando respuesta del bridge");
        }

        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<JsonRpcResponse>(json)
            ?? throw new BlsBridgeException("Respuesta JSON-RPC vacía o inválida");
    }

    private static Exception MapError(JsonRpcError error)
    {
        return error.Code switch
        {
            -32600 => new BlsBridgeException($"Invalid Request: {error.Message}"),
            -32601 => new BlsBridgeException($"Method Not Found: {error.Message}"),
            -32602 => new BlsBridgeException($"Invalid Params: {error.Message}"),
            -32603 => new BlsBridgeException($"Internal Error: {error.Message}"),
            -32000 => new BlsBridgeException($"Application Error: {error.Message}"),
            -32001 => new BlsBridgeException($"Frame Too Large: {error.Message}"),
            -32002 => new BlsBridgeException($"Tool Timeout: {error.Message}"),
            -32003 => new BlsBridgeException($"Frame Incomplete: {error.Message}"),
            -32004 => new BlsBridgeException($"Bridge Busy: {error.Message}"),
            _ => new BlsBridgeException($"Unknown Error ({error.Code}): {error.Message}")
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        _gate.Dispose();
    }
}
