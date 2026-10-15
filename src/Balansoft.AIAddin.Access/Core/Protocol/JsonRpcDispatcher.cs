// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Despacha solicitudes JSON-RPC a los handlers correspondientes.
/// Valida whitelist, parÃ¡metros y mapea excepciones a cÃ³digos de error.
/// </summary>
public sealed class JsonRpcDispatcher
{
    private readonly IBlsVbe _vbe;
    private readonly IBlsAccess _access;
    private readonly IUiScheduler _ui;
    private readonly IBlsLogger _logger;
    private readonly SemaphoreSlim _vbeGate = new(1, 1);

    public JsonRpcDispatcher(
        IBlsVbe vbe,
        IBlsAccess access,
        IUiScheduler ui,
        IBlsLogger logger)
    {
        _vbe = vbe;
        _access = access;
        _ui = ui;
        _logger = logger;
    }

    /// <summary>
    /// Despacha una solicitud y devuelve la respuesta.
    /// </summary>
    public async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        // ValidaciÃ³n JSON-RPC bÃ¡sica
        if (!string.Equals(request.JsonRpc, "2.0", StringComparison.Ordinal))
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.InvalidRequest,
                "jsonrpc debe ser '2.0'",
                new JsonRpcErrorData("InvalidRequest", null, null, null)));
        }

        // Whitelist
        if (!JsonRpcMethods.All.Any(m => string.Equals(m, request.Method, StringComparison.Ordinal)))
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.MethodNotFound,
                $"MÃ©todo no encontrado: {request.Method}",
                new JsonRpcErrorData("MethodNotFound", request.Method, null, null)));
        }

        try
        {
            return request.Method switch
            {
                JsonRpcMethods.ListModules       => await HandleVbeAsync(request, ct, () => _vbe.ListModules()),
                JsonRpcMethods.ReadModule        => await HandleVbeAsync(request, ct, () => _vbe.ReadModule(GetStringParam(request, "name"))),
                JsonRpcMethods.WriteModule       => await HandleVbeAsync(request, ct, async () => _vbe.WriteModule(GetStringParam(request, "name"), GetStringParam(request, "code"))),
                JsonRpcMethods.CreateModule      => await HandleVbeAsync(request, ct, async () => _vbe.CreateModule(
                    GetStringParam(request, "name"),
                    GetStringParam(request, "code", allowEmpty: true),
                    ParseComponentType(GetStringParam(request, "type")))),
                JsonRpcMethods.DeleteModule      => await HandleVbeAsync(request, ct, async () => _vbe.DeleteModule(
                    GetStringParam(request, "name"),
                    GetBoolParam(request, "confirm"))),
                JsonRpcMethods.GetSelection      => await HandleVbeAsync(request, ct, () => _vbe.GetSelection()),
                JsonRpcMethods.GetActiveModule   => await HandleVbeAsync(request, ct, () => _vbe.GetActiveModule()),
                JsonRpcMethods.ProjectSummary    => await HandleAccessAsync(request, ct, () => _access.ProjectSummary()),
                JsonRpcMethods.ListObjects       => await HandleAccessAsync(request, ct, () => _access.ListObjects()),
                JsonRpcMethods.ObjectDetail      => await HandleAccessAsync(request, ct, () => _access.ObjectDetail(
                    GetStringParam(request, "name"),
                    GetStringParam(request, "type"),
                    GetBoolParam(request, "includeSql", false),
                    GetIntParam(request, "maxFields", 200))),
                _ => JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                    JsonRpcErrorCodes.MethodNotFound,
                    $"MÃ©todo no implementado: {request.Method}",
                    new JsonRpcErrorData("MethodNotFound", request.Method, null, null)))
            };
        }
        catch (BlsInvalidParamsException ex)
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.InvalidParams,
                $"InvalidParamsException: {ex.Message}",
                new JsonRpcErrorData("InvalidParamsException", request.Method, null, GetStack())));
        }
        catch (BlsVbeUnavailableException ex)
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.ApplicationError,
                $"VbeUnavailableException: {ex.Message}",
                new JsonRpcErrorData("VbeUnavailableException", request.Method, null, GetStack())));
        }
        catch (BlsVbeException ex)
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.ApplicationError,
                $"VbeException: {ex.Message}",
                new JsonRpcErrorData("VbeException", request.Method, null, GetStack())));
        }
        catch (BlsAccessException ex)
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.ApplicationError,
                $"AccessException: {ex.Message}",
                new JsonRpcErrorData("AccessException", request.Method, null, GetStack())));
        }
        catch (BlsComException ex)
        {
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.ApplicationError,
                $"ComException: {ex.Message}",
                new JsonRpcErrorData("ComException", request.Method, ex.HResult.ToString("X8"), GetStack())));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Info("JsonRpcDispatcher", $"Cancelación de usuario en {request.Method}");
            return JsonRpcResponse.Cancelled(request.Id);
        }
        catch (Exception ex)
        {
            _logger.Error("JsonRpcDispatcher", $"Excepción no controlada en {request.Method}", ex);
            return JsonRpcResponse.CreateError(request.Id, new JsonRpcError(
                JsonRpcErrorCodes.InternalError,
                $"InternalError: {ex.Message}",
                new JsonRpcErrorData("InternalError", request.Method, null, GetStack())));
        }
    }

    private async Task<JsonRpcResponse> HandleVbeAsync(
        JsonRpcRequest request,
        CancellationToken ct,
        Func<Task> op)
    {
        if (ct.IsCancellationRequested)
            return JsonRpcResponse.Cancelled(request.Id);

        await _vbeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ui.RunAsync(op).ConfigureAwait(false);
            return JsonRpcResponse.Ok(request.Id);
        }
        finally
        {
            _vbeGate.Release();
        }
    }

    private async Task<JsonRpcResponse> HandleVbeAsync<T>(
        JsonRpcRequest request,
        CancellationToken ct,
        Func<T> op)
    {
        if (ct.IsCancellationRequested)
            return JsonRpcResponse.Cancelled(request.Id);

        await _vbeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await _ui.RunAsync(op).ConfigureAwait(false);
            return JsonRpcResponse.Ok(request.Id, result);
        }
        finally
        {
            _vbeGate.Release();
        }
    }

    private async Task<JsonRpcResponse> HandleAccessAsync(
        JsonRpcRequest request,
        CancellationToken ct,
        Func<Task> op)
    {
        if (ct.IsCancellationRequested)
            return JsonRpcResponse.Cancelled(request.Id);

        await _ui.RunAsync(op).ConfigureAwait(false);
        return JsonRpcResponse.Ok(request.Id);
    }

    private async Task<JsonRpcResponse> HandleAccessAsync<T>(
        JsonRpcRequest request,
        CancellationToken ct,
        Func<T> op)
    {
        if (ct.IsCancellationRequested)
            return JsonRpcResponse.Cancelled(request.Id);

        var result = await _ui.RunAsync(op).ConfigureAwait(false);
        return JsonRpcResponse.Ok(request.Id, result);
    }

    private string GetStringParam(JsonRpcRequest request, string name, bool allowEmpty = false)
    {
        if (!request.Params.HasValue || request.Params.Value.ValueKind == JsonValueKind.Null)
            throw new BlsInvalidParamsException($"Parámetro requerido: {name}");

        var elem = request.Params.Value;
        if (elem.ValueKind != JsonValueKind.Object)
            throw new BlsInvalidParamsException("params debe ser un objeto");

        if (!elem.TryGetProperty(name, out var prop))
            throw new BlsInvalidParamsException($"Parámetro requerido: {name}");

        if (prop.ValueKind != JsonValueKind.String)
            throw new BlsInvalidParamsException($"Parámetro '{name}' debe ser string");

        var value = prop.GetString();
        if (!allowEmpty && string.IsNullOrEmpty(value))
            throw new BlsInvalidParamsException($"Parámetro '{name}' no puede ser vacío");

        return value!;
    }

    private bool GetBoolParam(JsonRpcRequest request, string name, bool defaultValue = false)
    {
        if (!request.Params.HasValue || request.Params.Value.ValueKind == JsonValueKind.Null)
            return defaultValue;

        var elem = request.Params.Value;
        if (elem.ValueKind != JsonValueKind.Object)
            throw new BlsInvalidParamsException("params debe ser un objeto");

        if (!elem.TryGetProperty(name, out var prop))
            return defaultValue;

        if (prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False)
            throw new BlsInvalidParamsException($"Parámetro '{name}' debe ser boolean");

        return prop.GetBoolean();
    }

    private int GetIntParam(JsonRpcRequest request, string name, int defaultValue)
    {
        if (!request.Params.HasValue || request.Params.Value.ValueKind == JsonValueKind.Null)
            return defaultValue;

        var elem = request.Params.Value;
        if (elem.ValueKind != JsonValueKind.Object)
            throw new BlsInvalidParamsException("params debe ser un objeto");

        if (!elem.TryGetProperty(name, out var prop))
            return defaultValue;

        if (prop.ValueKind != JsonValueKind.Number)
            throw new BlsInvalidParamsException($"Parámetro '{name}' debe ser número");

        return prop.GetInt32();
    }

    private VbeComponentType ParseComponentType(string type)
    {
        return type switch
        {
            "Standard" => VbeComponentType.Standard,
            "Class"    => VbeComponentType.Class,
            "Form"     => VbeComponentType.Form,
            _ => throw new BlsInvalidParamsException($"type invÃ¡lido: {type}. VÃ¡lidos: Standard, Class, Form")
        };
    }

    private string? GetStack()
    {
#if DEBUG
        return Environment.StackTrace;
#else
        return null;
#endif
    }
}
