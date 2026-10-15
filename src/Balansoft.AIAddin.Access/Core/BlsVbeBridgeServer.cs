// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.VbeIntegration;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.UI;

namespace Balansoft.AIAddin.Core;

/// <summary>
/// Servidor JSON-RPC 2.0 sobre Named Pipe para el bridge VBE/Access.
/// </summary>
public sealed class BlsVbeBridgeServer : IBlsBridgeServer
{
    private const string DefaultPipeName = BlsPipeFraming.DefaultPipeName;

    /// <summary>
    /// Margen antes de liberar el pipe de un cliente rechazado por busy, para
    /// que el frame enviado llegue a leerse antes del EOF.
    /// </summary>
    private static readonly TimeSpan BusyDrainDelay = TimeSpan.FromMilliseconds(250);
    private readonly IBlsVbe _vbe;
    private readonly IBlsAccess _access;
    private readonly IUiScheduler _ui;
    private readonly IBlsLogger _logger;
    private readonly string _pipeName;
    private readonly JsonRpcDispatcher _dispatcher;
    private CancellationTokenSource? _loopCts;
    private Task? _acceptLoop;
    private volatile bool _isRunning;
    private readonly TaskCompletionSource<bool> _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private NamedPipeServerStream? _currentListeningPipe;

    public BlsVbeBridgeServer(
        IBlsVbe vbe,
        IBlsAccess access,
        IUiScheduler ui,
        IBlsLogger logger,
        string pipeName = DefaultPipeName)
    {
        _vbe = BlsContract.RequiresNotNull(vbe, nameof(vbe));
        _access = BlsContract.RequiresNotNull(access, nameof(access));
        _ui = BlsContract.RequiresNotNull(ui, nameof(ui));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
        _pipeName = BlsPipeFraming.NormalizePipeName(pipeName);
        _dispatcher = new JsonRpcDispatcher(vbe, access, ui, logger);
    }

    public bool IsRunning => _isRunning;

    /// <summary>
    /// Task que se completa cuando el servidor está listo para aceptar conexiones.
    /// </summary>
    public Task StartedAsync => _startedTcs.Task;

    public void Start()
    {
        if (_isRunning)
        {
            _logger.Warn("BlsVbeBridgeServer", "Start() llamado cuando ya está corriendo");
            return;
        }

        _loopCts = new CancellationTokenSource();
        _startedTcs.TrySetResult(false); // reset

        _logger.Info("BlsVbeBridgeServer", $"Arrancando bridge en {_pipeName}");
        _isRunning = true;

        _acceptLoop = Task.Factory.StartNew(RunAcceptLoopAsync, _loopCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (!_isRunning && _acceptLoop == null)
        {
            _logger.Info("BlsVbeBridgeServer", "StopAsync() llamado cuando no está corriendo");
            return;
        }

        _logger.Info("BlsVbeBridgeServer", "Deteniendo bridge...");
        _isRunning = false;
        _loopCts?.Cancel();
        try
        {
            _currentListeningPipe?.Close();
            _currentListeningPipe?.Dispose();
        }
        catch { }
        _currentListeningPipe = null;

        if (_acceptLoop != null)
        {
            var completed = await Task.WhenAny(_acceptLoop, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != _acceptLoop)
            {
                _logger.Warn("BlsVbeBridgeServer", $"Timeout ({timeout.TotalSeconds}s) esperando finalización del loop");
            }
            else
            {
                try { await _acceptLoop.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* esperado */ }
                catch (Exception ex)
                {
                    _logger.Error("BlsVbeBridgeServer", "Excepción en loop de aceptación", ex);
                }
            }
        }

        // Esperar a que terminen las tareas en background (con timeout)
        if (!_backgroundTasks.IsEmpty)
        {
            var backgroundTask = Task.WhenAll(_backgroundTasks.ToArray());
            var completed = await Task.WhenAny(backgroundTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (completed != backgroundTask)
            {
                _logger.Warn("BlsVbeBridgeServer", "Timeout esperando tareas en background");
            }
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _logger.Info("BlsVbeBridgeServer", "Bridge detenido");
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            try { StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch { /* ignore en Dispose */ }
        }
        _loopCts?.Dispose();
    }

    private async Task RunAcceptLoopAsync()
    {
        var cts = _loopCts;
        if (cts == null) return;

        var activeClientCount = 0;
        var clientCountLock = new object();

        // Señalar readiness tras crear la primera instancia.
        _startedTcs.TrySetResult(true);

        while (!cts.Token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe(cts.Token);
                if (pipe == null)
                {
                    // No se pudo crear una instancia (límite alcanzado o pipe en
                    // uso): se reintenta en vez de abandonar el loop.
                    await Task.Delay(50, cts.Token).ConfigureAwait(false);
                    continue;
                }

                _currentListeningPipe = pipe;
                await pipe.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                _currentListeningPipe = null;
                if (cts.Token.IsCancellationRequested)
                    break;

                bool isFirstClient;
                lock (clientCountLock)
                {
                    isFirstClient = activeClientCount == 0;
                    activeClientCount++;
                }

                if (!isFirstClient)
                {
                    // Solo un cliente activo: el segundo recibe busy y se cierra.
                    _logger.Warn("BlsVbeBridgeServer", "Segundo cliente conectado - enviando busy");
                    try
                    {
                        var error = new JsonRpcError(JsonRpcErrorCodes.Busy,
                            "Otro cliente ya está conectado",
                            new JsonRpcErrorData("Busy", null, null, null));
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(JsonRpcResponse.CreateError(0, error));
                        await BlsPipeFraming.WriteFrameAsync(pipe, bytes, cts.Token).ConfigureAwait(false);
                        await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("BlsVbeBridgeServer", "Error enviando busy", ex);
                    }

                    lock (clientCountLock) { activeClientCount--; }

                    // En modo mensaje, Dispose descarta lo que no se ha
                    // transferido: se espera un margen para que el cliente lea.
                    var busyPipe = pipe;
                    pipe = null;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(BusyDrainDelay, cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        finally
                        {
                            busyPipe.Dispose();
                        }
                    });
                }
                else
                {
                    _logger.Info("BlsVbeBridgeServer", "Cliente conectado");
                    var connectedPipe = pipe;
                    pipe = null;

                    // El cliente se atiende en background para no bloquear el loop.
                    var clientTask = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(connectedPipe, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("BlsVbeBridgeServer", "Error manejando cliente", ex);
                        }
                        finally
                        {
                            _logger.Info("BlsVbeBridgeServer", "Cliente desconectado");
                            connectedPipe?.Dispose();
                            lock (clientCountLock) { activeClientCount--; }
                        }
                    });

                    _backgroundTasks.Add(clientTask);
                }
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                pipe?.Dispose();
                break; // parada normal
            }
            catch (Exception ex)
            {
                _logger.Error("BlsVbeBridgeServer", "Error en loop de aceptación", ex);
                pipe?.Dispose();
                try
                {
                    await Task.Delay(250, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private NamedPipeServerStream? CreatePipe(CancellationToken ct)
    {
        try
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                2, // max 2 instancias simultáneas
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);

            return pipe;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error("BlsVbeBridgeServer", "Error creando instancia de pipe", ex);
            return null;
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                byte[]? payload;
                try
                {
                    payload = await BlsPipeFraming.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
                }
                catch (BlsFrameTooLargeException)
                {
                    await WriteErrorFrameAsync(pipe, null, JsonRpcErrorCodes.FrameTooLarge,
                        "frame_too_large", "FrameTooLarge", ct).ConfigureAwait(false);
                    break; // cerrar conexión
                }
                catch (BlsFrameIncompleteException)
                {
                    await WriteErrorFrameAsync(pipe, null, JsonRpcErrorCodes.FrameIncomplete,
                        "frame_incomplete", "FrameIncomplete", ct).ConfigureAwait(false);
                    break; // cerrar conexión
                }

                if (payload == null) // EOF limpio
                    break;

                if (payload.Length == 0)
                {
                    await WriteErrorFrameAsync(pipe, null, JsonRpcErrorCodes.InvalidRequest,
                        "empty_frame", "InvalidRequest", ct).ConfigureAwait(false);
                    continue;
                }

                JsonRpcRequest request;
                try
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(payload)
                        ?? throw new JsonException("Deserialización nula");
                }
                catch (JsonException ex)
                {
                    _logger.Warn("BlsVbeBridgeServer", $"JSON inválido: {ex.Message}");
                    await WriteErrorFrameAsync(pipe, null, JsonRpcErrorCodes.InvalidRequest,
                        "JSON inválido", "InvalidRequest", ct).ConfigureAwait(false);
                    continue;
                }

                var response = await _dispatcher.DispatchAsync(request, ct).ConfigureAwait(false);

                try
                {
                    var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    await BlsPipeFraming.WriteFrameAsync(pipe, responseBytes, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("BlsVbeBridgeServer", "Error manejando cliente", ex);
        }
    }

    private static async Task WriteErrorFrameAsync(
        Stream stream,
        object? id,
        int code,
        string message,
        string category,
        CancellationToken ct)
    {
        var error = new JsonRpcError(code, message, new JsonRpcErrorData(category, null, null, null));
        var response = JsonRpcResponse.CreateError(id ?? 0, error);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        await BlsPipeFraming.WriteFrameAsync(stream, bytes, ct).ConfigureAwait(false);
    }
}

