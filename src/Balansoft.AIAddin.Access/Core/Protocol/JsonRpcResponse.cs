// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Representa una respuesta JSON-RPC 2.0.
/// </summary>
/// <param name="JsonRpc">Siempre "2.0".</param>
/// <param name="Id">Identificador replicado de la solicitud.</param>
/// <param name="Result">Resultado exitoso (mutuamente exclusivo con <see cref="Error"/>).</param>
/// <param name="Error">InformaciÃ³n de error (mutuamente exclusivo con <see cref="Result"/>).</param>
public sealed record JsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("result")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Result,
    [property: JsonPropertyName("error")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonRpcError? Error)
{
    public static JsonRpcResponse Ok(object id, object? result = null)
    {
        var idElement = id switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            string s => JsonSerializer.SerializeToElement(s),
            int i => JsonSerializer.SerializeToElement(i),
            JsonElement je => je,
            _ => JsonSerializer.SerializeToElement(id?.ToString() ?? "0")
        };
        return new JsonRpcResponse("2.0", idElement, result, null);
    }

    public static JsonRpcResponse CreateError(object id, JsonRpcError error)
    {
        var idElement = id switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            string s => JsonSerializer.SerializeToElement(s),
            int i => JsonSerializer.SerializeToElement(i),
            JsonElement je => je,
            _ => JsonSerializer.SerializeToElement(id?.ToString() ?? "0")
        };
        return new JsonRpcResponse("2.0", idElement, null, error);
    }

    public static JsonRpcResponse Cancelled(object id)
    {
        var idElement = id switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            string s => JsonSerializer.SerializeToElement(s),
            int i => JsonSerializer.SerializeToElement(i),
            JsonElement je => je,
            _ => JsonSerializer.SerializeToElement(id?.ToString() ?? "0")
        };
        return new JsonRpcResponse("2.0", idElement, new { cancelled = true }, null);
    }
}
