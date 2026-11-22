// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: OpenTelemetry / ISO/IEC 27001 A.8.15
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsCorrelationContextTests
{
    [TestMethod]
    public void SinScope_CorrelationYTraceSonVacios()
    {
        Assert.AreEqual(string.Empty, BlsCorrelationContext.CorrelationId);
        Assert.AreEqual(string.Empty, BlsCorrelationContext.TraceId);
    }

    [TestMethod]
    public void Begin_GeneraIdsDe128Bits()
    {
        using var scope = BlsCorrelationContext.Begin();

        Assert.AreEqual(32, scope.CorrelationId.Length);
        Assert.AreEqual(32, scope.TraceId.Length);
        Assert.AreNotEqual(scope.CorrelationId, scope.TraceId);
    }

    [TestMethod]
    public void Begin_DosVeces_GeneraIdsDiferentes()
    {
        string first;
        using (var a = BlsCorrelationContext.Begin()) first = a.CorrelationId;
        using (var b = BlsCorrelationContext.Begin())
        {
            Assert.AreNotEqual(first, b.CorrelationId);
        }
    }

    [TestMethod]
    public void Begin_ConValoresExplícitos_LosExpone()
    {
        using var scope = BlsCorrelationContext.Begin("corr-123", "traza-456");

        Assert.AreEqual("corr-123", scope.CorrelationId);
        Assert.AreEqual("traza-456", scope.TraceId);
        Assert.AreEqual("corr-123", BlsCorrelationContext.CorrelationId);
    }

    [TestMethod]
    public void Begin_Anidado_AlDisponerRestauraElPadre()
    {
        using (BlsCorrelationContext.Begin("padre", "t-padre"))
        {
            using (BlsCorrelationContext.Begin("hijo", "t-hijo"))
            {
                Assert.AreEqual("hijo", BlsCorrelationContext.CorrelationId);
            }
            Assert.AreEqual("padre", BlsCorrelationContext.CorrelationId);
            Assert.AreEqual("t-padre", BlsCorrelationContext.TraceId);
        }
        Assert.AreEqual(string.Empty, BlsCorrelationContext.CorrelationId);
    }

    [TestMethod]
    public void Dispose_DosVeces_EsIdempotente()
    {
        var scope = BlsCorrelationContext.Begin("corr", "traza");

        scope.Dispose();
        scope.Dispose();

        Assert.AreEqual(string.Empty, BlsCorrelationContext.CorrelationId);
    }

    [TestMethod]
    public async Task Scope_FluyeAtravesDeAwait()
    {
        string dentro;
        using (BlsCorrelationContext.Begin("corr-async", "traza-async"))
        {
            await Task.Yield();
            dentro = BlsCorrelationContext.CorrelationId;
            await Task.Run(() => Assert.AreEqual("traza-async", BlsCorrelationContext.TraceId));
        }

        Assert.AreEqual("corr-async", dentro);
        Assert.AreEqual(string.Empty, BlsCorrelationContext.CorrelationId);
    }
}
