// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

[TestClass]
public class NimHttpRequestBuilderTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("NIM_MODEL", null);
    }

    [TestMethod]
    public void BuildJson_ModeloDefault_EsNemotron()
    {
        Environment.SetEnvironmentVariable("NIM_MODEL", null);

        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(NimHttpRequestBuilder.DefaultModel, doc.RootElement.GetProperty("model").GetString());
    }

    [TestMethod]
    public void BuildJson_ModeloParametro_TienePrioridadSobreEnv()
    {
        Environment.SetEnvironmentVariable("NIM_MODEL", "env-model");

        var json = Build("param-model", new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("param-model", doc.RootElement.GetProperty("model").GetString());
    }

    [TestMethod]
    public void BuildJson_ModeloEnv_SeUsaCuandoNoHayParametro()
    {
        Environment.SetEnvironmentVariable("NIM_MODEL", "env-model");

        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("env-model", doc.RootElement.GetProperty("model").GetString());
    }

    [TestMethod]
    public void BuildJson_SinTools_NoIncluyeToolsNiToolChoice()
    {
        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("tools", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("tool_choice", out _));
    }

    [TestMethod]
    public void BuildJson_ConTools_EnvuelveEnTypeFunction()
    {
        var schema = ParseSchema("{\"type\":\"object\",\"properties\":{}}");
        var tools = new List<ToolDefinition>
        {
            new ToolDefinition("write_module", "Escribe un módulo", schema)
        };

        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") }, tools);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("auto", doc.RootElement.GetProperty("tool_choice").GetString());

        var toolsEl = doc.RootElement.GetProperty("tools");
        Assert.AreEqual(JsonValueKind.Array, toolsEl.ValueKind);
        Assert.AreEqual(1, toolsEl.GetArrayLength());

        var first = toolsEl[0];
        Assert.AreEqual("function", first.GetProperty("type").GetString());

        var function = first.GetProperty("function");
        Assert.AreEqual("write_module", function.GetProperty("name").GetString());
        Assert.AreEqual("Escribe un módulo", function.GetProperty("description").GetString());
        Assert.AreEqual("object", function.GetProperty("parameters").GetProperty("type").GetString());
    }

    [TestMethod]
    public void BuildJson_StreamSiempreTrue()
    {
        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [TestMethod]
    public void BuildJson_TemperatureYMaxTokens_Fijos()
    {
        var json = Build(null, new List<ChatMessage> { new ChatMessage("user", "Hola") });

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0.2, doc.RootElement.GetProperty("temperature").GetDouble());
        Assert.AreEqual(8192, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [TestMethod]
    public void BuildJson_MensajesConservanRolYContenido()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage("system", "eres un asistente"),
            new ChatMessage("user", "Hola")
        };

        var json = Build(null, messages);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("messages");
        Assert.AreEqual(2, arr.GetArrayLength());
        Assert.AreEqual("system", arr[0].GetProperty("role").GetString());
        Assert.AreEqual("eres un asistente", arr[0].GetProperty("content").GetString());
        Assert.AreEqual("user", arr[1].GetProperty("role").GetString());
    }

    [TestMethod]
    public void BuildJson_MensajeTool_IncluyeToolCallId()
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.ToolResult("call_1", "{\"ok\":true}")
        };

        var json = Build(null, messages);

        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement.GetProperty("messages")[0];
        Assert.AreEqual("tool", msg.GetProperty("role").GetString());
        Assert.AreEqual("call_1", msg.GetProperty("tool_call_id").GetString());
    }

    [TestMethod]
    public void BuildJson_MensajeNormal_NoIncluyeToolCallId()
    {
        var messages = new List<ChatMessage> { new ChatMessage("user", "Hola") };

        var json = Build(null, messages);

        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement.GetProperty("messages")[0];
        Assert.IsFalse(msg.TryGetProperty("tool_call_id", out _));
        Assert.IsFalse(msg.TryGetProperty("tool_calls", out _));
    }

    [TestMethod]
    public void AssistantWithToolCalls_SerializaSobreFunction()
    {
        var call = new ToolCallComplete("call_1", "read_module", "{\"name\":\"M\"}");
        var message = ChatMessage.AssistantWithToolCalls(new List<ToolCallComplete> { call });

        var json = Build(null, new List<ChatMessage> { message });

        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement.GetProperty("messages")[0];
        Assert.AreEqual("assistant", msg.GetProperty("role").GetString());

        var toolCalls = msg.GetProperty("tool_calls");
        Assert.AreEqual(1, toolCalls.GetArrayLength());
        Assert.AreEqual("call_1", toolCalls[0].GetProperty("id").GetString());
        Assert.AreEqual("function", toolCalls[0].GetProperty("type").GetString());

        var function = toolCalls[0].GetProperty("function");
        Assert.AreEqual("read_module", function.GetProperty("name").GetString());
        Assert.AreEqual("{\"name\":\"M\"}", function.GetProperty("arguments").GetString());
    }

    private static string Build(string model, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools = null)
    {
        return NimHttpRequestBuilder.BuildJson(
            NimHttpRequestBuilder.ResolveModel(model),
            messages,
            tools ?? new List<ToolDefinition>());
    }

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

