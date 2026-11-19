// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 27001:2022
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Deriva la clave de idempotencia de un frame JSON-RPC. La correlación del
/// turno (ADR-016) delimita la operación lógica: un reenvío dentro del mismo
/// turno produce la misma clave y un turno distinto produce otra.
/// </summary>
public static class BlsIdempotencyKey
{
    /// <summary>
    /// Calcula SHA-256(correlation_id + "|" + method + "|" + params_json).
    /// Los parámetros no viajan en claro: solo el digest de 64 hex.
    /// </summary>
    public static string For(string method, JsonElement? parameters, string? correlationId)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(method, nameof(method));

        var canonicalParams = parameters.HasValue
            ? JsonSerializer.Serialize(parameters.Value)
            : "{}";
        var material = $"{correlationId ?? "anonymous"}|{method}|{canonicalParams}";

        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
    }
}
