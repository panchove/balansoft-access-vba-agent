// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

[TestClass]
public class NimMessageSanitizerTests
{
    [TestMethod]
    public void Sanitize_ToolHuerfanoSinToolCallId_GeneraAssistantYToolCallIdValido()
    {
        var rawMessages = new List<ChatMessage>
        {
            new ChatMessage("user", "analiza el codigo"),
            new ChatMessage("tool", "[\"Modulo1\"]"),
            new ChatMessage("user", "que contiene?")
        };

        var sanitized = NimMessageSanitizer.Sanitize(rawMessages);

        // Debe haber: user, assistant (con tool_calls), tool (con tool_call_id), user
        Assert.AreEqual(4, sanitized.Count);
        Assert.AreEqual("user", sanitized[0].Role);

        Assert.AreEqual("assistant", sanitized[1].Role);
        Assert.IsNotNull(sanitized[1].ToolCalls);
        Assert.AreEqual(1, sanitized[1].ToolCalls.Count);
        var callId = sanitized[1].ToolCalls[0].Id;
        Assert.IsFalse(string.IsNullOrWhiteSpace(callId));

        Assert.AreEqual("tool", sanitized[2].Role);
        Assert.AreEqual(callId, sanitized[2].ToolCallId);
        Assert.AreEqual("[\"Modulo1\"]", sanitized[2].Content);

        Assert.AreEqual("user", sanitized[3].Role);
    }

    [TestMethod]
    public void Sanitize_ToolHuerfanoConIdExistente_CreaAssistantConMismoId()
    {
        var rawMessages = new List<ChatMessage>
        {
            new ChatMessage("user", "analiza el codigo"),
            new ChatMessage("tool", "[\"Modulo1\"]") { ToolCallId = "call_abc123" },
            new ChatMessage("user", "que contiene?")
        };

        var sanitized = NimMessageSanitizer.Sanitize(rawMessages);

        Assert.AreEqual(4, sanitized.Count);
        Assert.AreEqual("assistant", sanitized[1].Role);
        Assert.AreEqual("call_abc123", sanitized[1].ToolCalls[0].Id);
        Assert.AreEqual("tool", sanitized[2].Role);
        Assert.AreEqual("call_abc123", sanitized[2].ToolCallId);
    }

    [TestMethod]
    public void Sanitize_AssistantConToolCallsSinRespuesta_CierraLlamadaPendiente()
    {
        var call = new ToolCallComplete("call_pend_1", "read_module", "{}");
        var assistant = ChatMessage.AssistantWithToolCalls(new[] { call });

        var rawMessages = new List<ChatMessage>
        {
            new ChatMessage("user", "lee modulo"),
            assistant,
            new ChatMessage("user", "cancela y haz otra cosa")
        };

        var sanitized = NimMessageSanitizer.Sanitize(rawMessages);

        // Debe haber: user, assistant, tool (sintetizado para cerrar call_pend_1), user
        Assert.AreEqual(4, sanitized.Count);
        Assert.AreEqual("assistant", sanitized[1].Role);
        Assert.AreEqual("tool", sanitized[2].Role);
        Assert.AreEqual("call_pend_1", sanitized[2].ToolCallId);
        Assert.AreEqual("user", sanitized[3].Role);
    }

    [TestMethod]
    public void BuildJson_ConToolHuerfano_SerializaToolCallIdValidoYPrecedidoPorAssistant()
    {
        var rawMessages = new List<ChatMessage>
        {
            new ChatMessage("user", "analiza el codigo"),
            new ChatMessage("tool", "[\"Modulo1\"]"),
            new ChatMessage("user", "que contiene?")
        };

        var json = NimHttpRequestBuilder.BuildJson("nvidia/nemotron-3-ultra-550b-a55b", rawMessages, null);

        using var doc = JsonDocument.Parse(json);
        var msgs = doc.RootElement.GetProperty("messages");
        Assert.AreEqual(4, msgs.GetArrayLength());

        var assistantMsg = msgs[1];
        Assert.AreEqual("assistant", assistantMsg.GetProperty("role").GetString());
        var toolCalls = assistantMsg.GetProperty("tool_calls");
        Assert.AreEqual(1, toolCalls.GetArrayLength());
        var id = toolCalls[0].GetProperty("id").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(id));

        var toolMsg = msgs[2];
        Assert.AreEqual("tool", toolMsg.GetProperty("role").GetString());
        Assert.AreEqual(id, toolMsg.GetProperty("tool_call_id").GetString());
    }

    [TestMethod]
    public void HistoryMessage_ToolCalls_SerializaYDeserializaCorrectamente()
    {
        var historyDoc = new HistoryDocument
        {
            ProjectKey = "test-key",
            Messages = new List<HistoryMessage>
            {
                new HistoryMessage
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = new List<HistoryToolCall>
                    {
                        new HistoryToolCall
                        {
                            Id = "call_test",
                            Name = "list_modules",
                            ArgumentsJson = "{}"
                        }
                    }
                },
                new HistoryMessage
                {
                    Role = "tool",
                    Content = "[\"Modulo1\"]",
                    ToolCallId = "call_test",
                    ToolName = "list_modules"
                }
            }
        };

        var json = JsonSerializer.Serialize(historyDoc);
        var restored = JsonSerializer.Deserialize<HistoryDocument>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(2, restored.Messages.Count);
        Assert.IsNotNull(restored.Messages[0].ToolCalls);
        Assert.AreEqual(1, restored.Messages[0].ToolCalls.Count);
        Assert.AreEqual("call_test", restored.Messages[0].ToolCalls[0].Id);
        Assert.AreEqual("list_modules", restored.Messages[0].ToolCalls[0].Name);
        Assert.AreEqual("call_test", restored.Messages[1].ToolCallId);
    }
}
