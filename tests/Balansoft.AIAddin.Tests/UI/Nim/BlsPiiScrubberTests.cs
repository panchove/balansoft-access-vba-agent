// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

/// <summary>F5 / R-03 — saneamiento determinista antes de enviar a NIM.</summary>
[TestClass]
public class BlsPiiScrubberTests
{
    private static IReadOnlyList<ChatMessage> User(string text)
        => new List<ChatMessage> { new ChatMessage("user", text) };

    [TestMethod]
    public void Scrub_Email_RedactaYSoloExponeCategoria()
    {
        var result = BlsPiiScrubber.Scrub(User("Escribe a juan.perez@ejemplo.com, por favor"));

        StringAssert.Contains(result.Messages[0].Content, "[EMAIL]");
        Assert.IsFalse(result.Messages[0].Content.Contains("juan.perez@ejemplo.com"));
        Assert.IsTrue(result.Findings.Any(f => f.Category == "EMAIL"));
        Assert.IsTrue(result.Findings.All(f => !f.Token.Contains("@")));
    }

    [TestMethod]
    public void Scrub_ConnectionString_RedactaCredencialesCompletas()
    {
        const string conn = "Provider=SQLOLEDB;Server=db01;Database=Ventas;User Id=sa;Password=P@ssw0rd;";

        var result = BlsPiiScrubber.Scrub(User("Cadena: " + conn));

        StringAssert.Contains(result.Messages[0].Content, "[CONNECTION_STRING]");
        Assert.IsFalse(result.Messages[0].Content.Contains("P@ssw0rd"));
        Assert.IsFalse(result.Messages[0].Content.Contains("db01"));
        Assert.IsTrue(result.Findings.Any(f => f.Category == "CONNECTION_STRING"));
    }

    [TestMethod]
    public void Scrub_NvidiaKey_Redacta()
    {
        var result = BlsPiiScrubber.Scrub(User("Usa nvapi-abcdefgh12345678 en el header"));

        StringAssert.Contains(result.Messages[0].Content, "[REDACTED]");
        Assert.IsFalse(result.Messages[0].Content.Contains("nvapi-abcdefgh12345678"));
    }

    [TestMethod]
    public void Scrub_UserPath_Redacta()
    {
        var result = BlsPiiScrubber.Scrub(User("El archivo está en C:\\Users\\juan\\Documents\\qr.txt"));

        StringAssert.Contains(result.Messages[0].Content, "[USER_PATH]");
        Assert.IsFalse(result.Messages[0].Content.Contains("juan"));
        Assert.IsTrue(result.Findings.Any(f => f.Category == "USER_PATH"));
    }

    [TestMethod]
    public void Scrub_Ipv4YTelefono_RedactaAmbos()
    {
        var result = BlsPiiScrubber.Scrub(User("Servidor 10.0.0.5, teléfono +34 612 345 678"));

        StringAssert.Contains(result.Messages[0].Content, "[IPV4]");
        StringAssert.Contains(result.Messages[0].Content, "[PHONE]");
        Assert.IsFalse(result.Messages[0].Content.Contains("10.0.0.5"));
        Assert.IsTrue(result.Findings.Any(f => f.Category == "IPV4"));
        Assert.IsTrue(result.Findings.Any(f => f.Category == "PHONE"));
    }

    [TestMethod]
    public void Scrub_ToolCallArguments_RedactaSinMutarOriginal()
    {
        const string originalArgs = "{\"name\":\"juan@ejemplo.com\"}";
        var messages = new List<ChatMessage>
        {
            new ChatMessage("assistant", string.Empty)
            {
                ToolCalls = new List<AssistantToolCall>
                {
                    new AssistantToolCall
                    {
                        Id = "call_1",
                        Function = new AssistantToolCallFunction
                        {
                            Name = "read_module",
                            Arguments = originalArgs
                        }
                    }
                }
            }
        };

        var result = BlsPiiScrubber.Scrub(messages);

        var args = result.Messages[0].ToolCalls![0].Function.Arguments;
        StringAssert.Contains(args, "[EMAIL]");
        Assert.IsFalse(args.Contains("juan@ejemplo.com"));
        Assert.AreEqual(originalArgs, messages[0].ToolCalls![0].Function.Arguments,
            "El mensaje original no se muta: solo se sanea la copia saliente.");
    }

    [TestMethod]
    public void Scrub_Idempotente_SegundaPasadaNoCambiaElResultado()
    {
        var first = BlsPiiScrubber.Scrub(User("Correo juan@ejemplo.com y IP 192.168.1.10"));

        var second = BlsPiiScrubber.Scrub(first.Messages);

        Assert.AreEqual(
            JsonSerializer.Serialize(first.Messages),
            JsonSerializer.Serialize(second.Messages));
        Assert.AreEqual(0, second.Findings.Count, "Los tokens resultantes no vuelven a coincidir.");
    }

    [TestMethod]
    public void Scrub_CodigoVbaSinPII_SinHallazgosYContenidoIgual()
    {
        const string code = "Public Function Suma(a As Long, b As Long) As Long\n  Suma = a + b\nEnd Function";

        var result = BlsPiiScrubber.Scrub(User(code));

        Assert.AreEqual(0, result.Findings.Count);
        Assert.AreEqual(code, result.Messages[0].Content);
    }
}
