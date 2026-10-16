// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 29148:2018
// Architecture Reference: P12 (Español en UI y logs)

using System;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsStringsTests
{
    [TestMethod]
    public void Get_SpanishKey_ReturnsSpanishText()
    {
        BlsStrings.CurrentLanguage = "es";
        var text = BlsStrings.Get("AppTitle");
        Assert.AreEqual("NIM Terminal", text);
    }

    [TestMethod]
    public void Get_WithFormatArgs_ReturnsFormattedText()
    {
        BlsStrings.CurrentLanguage = "es";
        var text = BlsStrings.Get("MsgSessionLoaded", "Mi sesión");
        Assert.AreEqual("Sesión cargada: Mi sesión", text);
    }

    [TestMethod]
    public void Get_UnknownKey_ReturnsKey()
    {
        BlsStrings.CurrentLanguage = "es";
        var text = BlsStrings.Get("ClaveQueNoExiste");
        Assert.AreEqual("ClaveQueNoExiste", text);
    }

    [TestMethod]
    public void SwitchToEnglish_ReturnsEnglishText()
    {
        BlsStrings.CurrentLanguage = "en";
        var text = BlsStrings.Get("AppTitle");
        Assert.AreEqual("NIM Terminal", text);

        var btn = BlsStrings.Get("BtnNewSession");
        Assert.AreEqual("+ New", btn);
    }

    [TestMethod]
    public void FallbackToSpanishWhenKeyMissingInEnglish()
    {
        BlsStrings.CurrentLanguage = "en";
        // "MsgSoloEspanol" solo existe en español, debe hacer fallback
        var text = BlsStrings.Get("MsgSoloEspanol");
        Assert.AreEqual("Este texto solo existe en español", text);
    }

    [TestMethod]
    public void IsLanguageSupported_ReturnsTrueForSupported()
    {
        Assert.IsTrue(BlsStrings.IsLanguageSupported("es"));
        Assert.IsTrue(BlsStrings.IsLanguageSupported("en"));
    }

    [TestMethod]
    public void IsLanguageSupported_ReturnsFalseForUnsupported()
    {
        Assert.IsFalse(BlsStrings.IsLanguageSupported("fr"));
        Assert.IsFalse(BlsStrings.IsLanguageSupported(""));
        Assert.IsFalse(BlsStrings.IsLanguageSupported(null!));
    }

    [TestMethod]
    public void SetLanguage_ThrowsForUnsupported()
    {
        BlsStrings.CurrentLanguage = "es";
        Assert.ThrowsException<ArgumentException>(() => BlsStrings.CurrentLanguage = "fr");
    }

    [TestMethod]
    public void SetLanguage_ThrowsForEmpty()
    {
        BlsStrings.CurrentLanguage = "es";
        Assert.ThrowsException<ArgumentException>(() => BlsStrings.CurrentLanguage = "");
    }

    [TestMethod]
    public void Get_NewColumnKeys_ReturnsLocalized()
    {
        BlsStrings.CurrentLanguage = "es";
        Assert.AreEqual("Rol", BlsStrings.Get("ColumnRole"));
        Assert.AreEqual("Mensaje", BlsStrings.Get("ColumnMessage"));

        BlsStrings.CurrentLanguage = "en";
        Assert.AreEqual("Role", BlsStrings.Get("ColumnRole"));
        Assert.AreEqual("Message", BlsStrings.Get("ColumnMessage"));
    }

    [TestMethod]
    public void Get_MultipleFormatArgs_ReturnsFormatted()
    {
        BlsStrings.CurrentLanguage = "es";
        var text = BlsStrings.Get("MsgContextHeader", "Mod1", "Clase", 42, " [Truncado]");
        Assert.AreEqual("[Contexto VBE Actual]\nMódulo: Mod1 (Clase)\nSelección: 42 caracteres [Truncado]", text);

        BlsStrings.CurrentLanguage = "en";
        text = BlsStrings.Get("MsgContextHeader", "Mod1", "Class", 42, " [Truncated]");
        Assert.AreEqual("[Current VBE Context]\nModule: Mod1 (Class)\nSelection: 42 characters [Truncated]", text);
    }

    [TestMethod]
    public void Get_PromptKeys_ReturnsLocalized()
    {
        BlsStrings.CurrentLanguage = "es";
        var prompt = BlsStrings.Get("PromptExplain");
        Assert.IsTrue(prompt.StartsWith("Por favor, explica"));

        BlsStrings.CurrentLanguage = "en";
        prompt = BlsStrings.Get("PromptExplain");
        Assert.IsTrue(prompt.StartsWith("Please explain"));
    }

    [TestMethod]
    public void Get_TooltipKeys_ReturnsLocalized()
    {
        BlsStrings.CurrentLanguage = "es";
        Assert.AreEqual("Explicar código activo", BlsStrings.Get("TooltipExplain"));
        Assert.AreEqual("Eliminar", BlsStrings.Get("BtnDeleteSession"));

        BlsStrings.CurrentLanguage = "en";
        Assert.AreEqual("Explain active code", BlsStrings.Get("TooltipExplain"));
        Assert.AreEqual("Delete", BlsStrings.Get("BtnDeleteSession"));
    }
}
