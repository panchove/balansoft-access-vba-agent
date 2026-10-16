// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Documento de historial de chat para un proyecto y sesión.
/// </summary>
public sealed class HistoryDocument
{
    public int Version { get; set; } = 1;
    public string ProjectKey { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "Nueva sesión";
    public bool Ephemeral { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<HistoryMessage> Messages { get; set; } = new();
}

/// <summary>
/// Metadatos resumidos de una sesión de chat para el catálogo del proyecto.
/// </summary>
public sealed class SessionMetadata
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Nueva sesión";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }

    public override string ToString() => $"{UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} - {Title}";
}

/// <summary>
/// Mensaje individual en el historial.
/// </summary>
public sealed class HistoryMessage
{
    public string Role { get; set; } = "";      // user | assistant | tool
    public string Content { get; set; } = "";
    public DateTime Ts { get; set; }
    public bool Cancelled { get; set; }
    public string? ToolName { get; set; }       // solo si Role == "tool"
    public string? ToolCallId { get; set; }     // solo si Role == "tool"

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HistoryToolCall>? ToolCalls { get; set; } // solo si Role == "assistant" y llamo herramientas
}

/// <summary>
/// Representacion serializable de una tool call realizada por el asistente en el historial.
/// </summary>
public sealed class HistoryToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
}
