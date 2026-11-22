// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

/// <summary>F5 / R-03 — el saneamiento se aplica siempre en la frontera HTTP.</summary>
[TestClass]
public class NimPiiScrubbingTests
{
    private static string Build(IReadOnlyList<ChatMessage> messages)
        => NimHttpRequestBuilder.BuildJson(
            NimHttpRequestBuilder.DefaultModel,
            messages,
            new List<ToolDefinition>());

    [TestMethod]
    public void BuildJson_EmailEnMensaje_RedactaYNoMutaOriginal()
    {
        var original = "Escribe a juan.perez@ejemplo.com";
        var messages = new List<ChatMessage> { new ChatMessage("user", original) };

        var json = Build(messages);

        StringAssert.Contains(json, "[EMAIL]");
        Assert.IsFalse(json.Contains("juan.perez@ejemplo.com"));
        Assert.AreEqual(original, messages[0].Content,
            "El historial local conserva el contenido original.");
    }

    [TestMethod]
    public void BuildJson_ToolCallArguments_RedactaAntesDeSerializar()
    {
        var call = new ToolCallComplete("call_1", "read_module", "{\"to\":\"ana@ejemplo.com\"}");
        var messages = new List<ChatMessage> { ChatMessage.AssistantWithToolCalls(new[] { call }) };

        var json = Build(messages);

        StringAssert.Contains(json, "[EMAIL]");
        Assert.IsFalse(json.Contains("ana@ejemplo.com"));
    }

    [TestMethod]
    public void BuildJson_ConnectionString_NoApareceEnElPayload()
    {
        const string conn = "Server=db01;Database=Ventas;User Id=sa;Password=P@ssw0rd;";
        var messages = new List<ChatMessage> { new ChatMessage("user", "Cadena: " + conn) };

        var json = Build(messages);

        Assert.IsFalse(json.Contains("P@ssw0rd"));
        StringAssert.Contains(json, "[CONNECTION_STRING]");
    }

    [TestMethod]
    public void BuildJson_CodigoVbaSinPII_QuedaIntacto()
    {
        const string code = "Public Sub Test()\n  MsgBox \"hola\"\nEnd Sub";
        var messages = new List<ChatMessage> { new ChatMessage("user", code) };

        var json = Build(messages);

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.AreEqual(code, content);
    }
}
