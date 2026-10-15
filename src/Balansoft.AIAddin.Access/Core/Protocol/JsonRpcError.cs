// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Estructura de error JSON-RPC 2.0 con datos extendidos.
/// </summary>
/// <param name="Code">Código de error (ver <see cref="JsonRpcErrorCodes"/>).</param>
/// <param name="Message">Mensaje legible: categoría + mensaje original.</param>
/// <param name="Data">Datos extendidos con categoría, operación, HRESULT y stack (solo Debug).</param>
public sealed record JsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] JsonRpcErrorData? Data);
