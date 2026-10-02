// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Mensaje de chat para la API NIM.
/// </summary>
/// <param name="Role">Rol: "system", "user", "assistant" o "tool".</param>
/// <param name="Content">Contenido del mensaje.</param>
public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    /// <summary>
    /// Requerido para role="tool": identifica la llamada que responde.
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ToolCallId { get; init; }

    /// <summary>
    /// Presente en role="assistant" cuando el modelo pidiÃ³ herramientas.
    /// </summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AssistantToolCall> ToolCalls { get; init; }

    /// <summary>
    /// Crea el mensaje assistant que introduce las tool calls del turno.
    /// </summary>
    public static ChatMessage AssistantWithToolCalls(IReadOnlyList<ToolCallComplete> calls)
    {
        var list = new List<AssistantToolCall>();
        if (calls != null)
        {
            foreach (var call in calls)
                list.Add(new AssistantToolCall(call));
        }

        return new ChatMessage("assistant", string.Empty) { ToolCalls = list };
    }

    /// <summary>
    /// Crea el mensaje role="tool" con el resultado de una herramienta.
    /// </summary>
    public static ChatMessage ToolResult(string toolCallId, string content)
    {
        return new ChatMessage("tool", content) { ToolCallId = toolCallId };
    }
}

/// <summary>
/// Tool call en el sobre { id, type, function } que espera la API.
/// </summary>
public sealed class AssistantToolCall
{
    public AssistantToolCall()
    {
        Id = string.Empty;
        Function = new AssistantToolCallFunction();
    }

    public AssistantToolCall(ToolCallComplete call)
    {
        Id = call?.Id ?? string.Empty;
        Function = new AssistantToolCallFunction
        {
            Name = call?.Name ?? string.Empty,
            Arguments = call?.ArgumentsJson ?? "{}"
        };
    }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public AssistantToolCallFunction Function { get; set; }
}

/// <summary>
/// Nombre y argumentos de una tool call del asistente.
/// </summary>
public sealed class AssistantToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

