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
}
