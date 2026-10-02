// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// DefiniciÃ³n de una herramienta (tool) para function calling.
/// </summary>
/// <param name="Name">Nombre de la funciÃ³n.</param>
/// <param name="Description">DescripciÃ³n para el modelo.</param>
/// <param name="ParametersSchema">Schema JSON de los parÃ¡metros (tipo object).</param>
public sealed record ToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement ParametersSchema);
