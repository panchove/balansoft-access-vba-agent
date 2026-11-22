// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Representa una solicitud JSON-RPC 2.0 entrante.
/// </summary>
/// <param name="JsonRpc">Debe ser exactamente "2.0".</param>
/// <param name="Id">Identificador de la solicitud (number o string). Se replica tal cual en la respuesta.</param>
/// <param name="Method">Nombre del método a invocar.</param>
/// <param name="Params">Parámetros opcionales como <see cref="JsonElement"/> para validación estricta.</param>
/// <param name="CorrelationId">F3: identificador del turno, propagado al log del dispatcher.</param>
/// <param name="TraceId">F3: identificador de traza, compartido con el cliente.</param>
/// <param name="IdempotencyKey">F4: clave derivada del turno para deduplicar escrituras reenviadas.</param>
/// <param name="AuthorizationToken">HITL: token de un solo uso emitido tras la aprobacion humana en la terminal.</param>
public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("correlation_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId = null,
    [property: JsonPropertyName("trace_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TraceId = null,
    [property: JsonPropertyName("idempotency_key")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null,
    [property: JsonPropertyName("authorization_token")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorizationToken = null)
{
    /// <summary>
    /// Obtiene el Id como objeto (long, string, etc.) para uso interno.
    /// </summary>
    public object GetIdAsObject()
    {
        return Id.ValueKind switch
        {
            JsonValueKind.Number => Id.GetInt64(),
            JsonValueKind.String => Id.GetString()!,
            JsonValueKind.Null => 0L,
            _ => Id.GetRawText()
        };
    }
}
