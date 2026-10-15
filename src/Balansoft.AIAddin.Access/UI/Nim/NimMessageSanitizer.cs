// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Sanea y valida secuencias de mensajes para la API de NIM / OpenAI chat completions,
/// garantizando que tool_calls y tool results mantengan la correspondencia estricta exigida por el endpoint.
/// </summary>
public static class NimMessageSanitizer
{
    /// <summary>
    /// Sanea una coleccion de mensajes para cumplir el protocolo de chat completions de NIM.
    /// </summary>
    /// <param name="messages">Mensajes originales (pueden venir de un historial antiguo o incompleto).</param>
    /// <returns>Lista de mensajes normalizada y garantizada contra errores 400 de tool_call_id.</returns>
    public static List<ChatMessage> Sanitize(IReadOnlyList<ChatMessage> messages)
    {
        BlsContract.RequiresNotNull(messages, nameof(messages));

        var result = new List<ChatMessage>();
        var pendingCallIds = new HashSet<string>(StringComparer.Ordinal);
        var synthCounter = 1;

        foreach (var msg in messages)
        {
            if (msg == null) continue;

            if (msg.Role == "user" || msg.Role == "system")
            {
                // Si habia llamadas a herramientas pendientes sin responder por parte de un assistant anterior,
                // las cerramos con una respuesta dummy para que NIM no rechace la peticion con HTTP 400.
                ClosePendingToolCalls(result, pendingCallIds);
                result.Add(msg);
            }
            else if (msg.Role == "assistant")
            {
                // Cerrar cualquier llamada pendiente antes de introducir un nuevo mensaje assistant
                ClosePendingToolCalls(result, pendingCallIds);

                result.Add(msg);

                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var id = !string.IsNullOrWhiteSpace(tc.Id) ? tc.Id : $"call_auto_{synthCounter++}";
                        tc.Id = id;
                        pendingCallIds.Add(id);
                    }
                }
            }
            else if (msg.Role == "tool")
            {
                var toolCallId = msg.ToolCallId;
                if (string.IsNullOrWhiteSpace(toolCallId))
                {
                    // Si el mensaje tool no tenia ID pero hay una llamada pendiente, asociarla
                    if (pendingCallIds.Count > 0)
                    {
                        toolCallId = pendingCallIds.First();
                    }
                    else
                    {
                        toolCallId = $"call_synth_{synthCounter++}";
                    }
                }

                // Verificar si el mensaje inmediatamente anterior es un assistant que contiene esta llamada
                var precedingAssistant = result.LastOrDefault() as ChatMessage;
                var hasMatchingAssistant = precedingAssistant != null
                    && precedingAssistant.Role == "assistant"
                    && precedingAssistant.ToolCalls != null
                    && precedingAssistant.ToolCalls.Any(tc => string.Equals(tc.Id, toolCallId, StringComparison.Ordinal));

                if (!hasMatchingAssistant)
                {
                    // Es un tool huerfano (por ejemplo de un historial guardado con una version anterior).
                    // Sintetizamos el mensaje assistant invocador para que NIM acepte la respuesta.
                    var synthToolCall = new AssistantToolCall
                    {
                        Id = toolCallId,
                        Type = "function",
                        Function = new AssistantToolCallFunction
                        {
                            Name = "vbe_tool",
                            Arguments = "{}"
                        }
                    };

                    if (precedingAssistant != null && precedingAssistant.Role == "assistant" && precedingAssistant.ToolCalls != null)
                    {
                        // Añadir al assistant existente
                        var updatedCalls = new List<AssistantToolCall>(precedingAssistant.ToolCalls) { synthToolCall };
                        var updatedAssistant = precedingAssistant with { ToolCalls = updatedCalls };
                        result[result.Count - 1] = updatedAssistant;
                    }
                    else
                    {
                        // Crear nuevo mensaje assistant invocador
                        result.Add(new ChatMessage("assistant", string.Empty)
                        {
                            ToolCalls = new List<AssistantToolCall> { synthToolCall }
                        });
                    }
                }

                pendingCallIds.Remove(toolCallId);
                result.Add(msg with { ToolCallId = toolCallId });
            }
            else
            {
                result.Add(msg);
            }
        }

        // Si quedan llamadas pendientes al final de la secuencia antes de enviar el nuevo turno
        ClosePendingToolCalls(result, pendingCallIds);

        return result;
    }

    private static void ClosePendingToolCalls(List<ChatMessage> result, HashSet<string> pendingCallIds)
    {
        if (pendingCallIds.Count == 0) return;

        foreach (var callId in pendingCallIds.ToList())
        {
            result.Add(new ChatMessage("tool", "{\"status\":\"cancelled\"}")
            {
                ToolCallId = callId
            });
        }

        pendingCallIds.Clear();
    }
}
