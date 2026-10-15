// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Tool call completo ensamblado desde fragmentos SSE.
/// </summary>
/// <param name="Id">ID de la llamada (ej. "call_x").</param>
/// <param name="Name">Nombre de la función.</param>
/// <param name="ArgumentsJson">Argumentos JSON completos.</param>
public sealed record ToolCallComplete(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string ArgumentsJson);
