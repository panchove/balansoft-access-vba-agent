// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Documento de historial de chat para un proyecto.
/// </summary>
public sealed class HistoryDocument
{
    public int Version { get; set; } = 1;
    public string ProjectKey { get; set; } = "";
    public bool Ephemeral { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<HistoryMessage> Messages { get; set; } = new();
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
}
