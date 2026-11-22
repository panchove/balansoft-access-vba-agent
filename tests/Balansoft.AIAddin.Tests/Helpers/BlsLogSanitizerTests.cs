// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 27001 A.8.24 / ISO/IEC 27701 / Pilar 24
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Runtime.InteropServices;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsLogSanitizerTests
{
    [TestMethod]
    public void Sanitize_NvidiaKey_Redacta()
    {
        var result = BlsLogSanitizer.Sanitize("key=nvapi-abcdefgh12345678 fine");

        Assert.IsFalse(result.Contains("nvapi-abcdefgh12345678"));
        StringAssert.Contains(result, "[REDACTED]");
    }

    [TestMethod]
    public void Sanitize_BearerToken_Redacta()
    {
        var result = BlsLogSanitizer.Sanitize("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0");

        Assert.IsFalse(result.Contains("eyJhbGciOiJIUzI1NiJ9"));
        Assert.IsFalse(result.Contains("eyJzdWIiOiIxIn0"));
        StringAssert.Contains(result, "[REDACTED]");
    }

    [TestMethod]
    public void Sanitize_ApiKeyConGuionBajo_RedactaValorPeroConservaNombre()
    {
        var result = BlsLogSanitizer.Sanitize("NIM_API_KEY=secreto_123456 fin");

        Assert.IsFalse(result.Contains("secreto_123456"));
        StringAssert.Contains(result, "NIM_API_KEY=");
        StringAssert.Contains(result, "[REDACTED]");
    }

    [TestMethod]
    public void Sanitize_GenericSkKey_Redacta()
    {
        var result = BlsLogSanitizer.Sanitize("token sk-abcdefghijklmnop1234 listo");

        Assert.IsFalse(result.Contains("sk-abcdefghijklmnop1234"));
        StringAssert.Contains(result, "[REDACTED]");
    }

    [TestMethod]
    public void Sanitize_Contrasena_Redacta()
    {
        var result = BlsLogSanitizer.Sanitize("password: supersecreto; ok");

        Assert.IsFalse(result.Contains("supersecreto"));
        StringAssert.Contains(result, "password=[REDACTED]");
    }

    [TestMethod]
    public void Sanitize_SaltosDeLinea_LosEscapa()
    {
        var result = BlsLogSanitizer.Sanitize("linea1\nlinea2\r\nlinea3");

        Assert.AreEqual("linea1\\nlinea2\\nlinea3", result);
    }

    [TestMethod]
    public void Sanitize_MensajeLargo_Trunca()
    {
        var result = BlsLogSanitizer.Sanitize(new string('a', 5000));

        Assert.IsTrue(result.Length <= BlsLogSanitizer.MaxMessageLength);
        StringAssert.EndsWith(result, "[TRUNCATED]");
    }

    [TestMethod]
    public void Sanitize_Null_RetornaCadenaVacia()
    {
        Assert.AreEqual(string.Empty, BlsLogSanitizer.Sanitize(null));
    }

    [TestMethod]
    public void DescribeException_ComException_IncluyeHResultEnHex()
    {
        var ex = new COMException("fallo COM", unchecked((int)0x80040154));

        var detail = BlsLogSanitizer.DescribeException(ex);

        Assert.AreEqual("COMException", detail.Type);
        Assert.AreEqual("80040154", detail.HResult);
    }

    [TestMethod]
    public void DescribeException_NoIncluyeStackYRedactaMensaje()
    {
        var ex = new InvalidOperationException("password = abc123456");

        var detail = BlsLogSanitizer.DescribeException(ex);

        Assert.IsFalse(detail.Message.Contains("abc123456"));
        Assert.IsNull(detail.HResult);
        Assert.IsFalse(detail.Message.Contains("   at "));
    }
}
