// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / Pilar 7 (calidad medible)
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System.Linq;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsSloMetricsTests
{
    [TestMethod]
    public void Snapshot_SinMuestras_ValoresNeutralesYDentroDeSlo()
    {
        var snapshot = new BlsSloMetrics().Snapshot();

        Assert.AreEqual(0, snapshot.TotalWrites);
        Assert.AreEqual(1.0, snapshot.WriteSuccessRate, 0.0001);
        Assert.AreEqual(0.0, snapshot.WriteP95Ms, 0.0001);
        Assert.AreEqual(1.0, snapshot.TurnSuccessRate, 0.0001);
        Assert.IsTrue(snapshot.IsWithinSlo);
    }

    [TestMethod]
    public void RecordWrite_TodasCorrectas_DentroDeSlo()
    {
        var metrics = new BlsSloMetrics();
        foreach (var _ in Enumerable.Range(0, 100)) metrics.RecordWrite(1.0, true);

        var snapshot = metrics.Snapshot();
        Assert.AreEqual(100, snapshot.TotalWrites);
        Assert.AreEqual(0, snapshot.FailedWrites);
        Assert.AreEqual(1.0, snapshot.WriteSuccessRate, 0.0001);
        Assert.IsTrue(snapshot.IsWithinSlo);
    }

    [TestMethod]
    public void RecordWrite_UnFalloDeCien_RompeElSlo()
    {
        var metrics = new BlsSloMetrics();
        foreach (var _ in Enumerable.Range(0, 99)) metrics.RecordWrite(1.0, true);
        metrics.RecordWrite(1.0, false);

        var snapshot = metrics.Snapshot();
        Assert.AreEqual(0.99, snapshot.WriteSuccessRate, 0.0001);
        Assert.IsFalse(snapshot.IsWithinSlo);
    }

    [TestMethod]
    public void RecordWrite_LatenciaP95_SeCalcula()
    {
        var metrics = new BlsSloMetrics();
        foreach (var ms in Enumerable.Range(1, 100)) metrics.RecordWrite(ms, true);

        Assert.AreEqual(95.0, metrics.Snapshot().WriteP95Ms, 0.0001);
    }

    [TestMethod]
    public void RecordWrite_LatenciaPorEncimaDelObjetivo_RompeElSlo()
    {
        var metrics = new BlsSloMetrics();
        metrics.RecordWrite(200.0, true);

        var snapshot = metrics.Snapshot();
        Assert.IsFalse(snapshot.IsWithinSlo);
        Assert.AreEqual(200.0, snapshot.WriteP95Ms, 0.0001);
    }

    [TestMethod]
    public void RecordTurn_DiecinueveDeVeinte_EstaEnElLimite()
    {
        var metrics = new BlsSloMetrics();
        foreach (var _ in Enumerable.Range(0, 19)) metrics.RecordTurn(true);
        metrics.RecordTurn(false);

        var snapshot = metrics.Snapshot();
        Assert.AreEqual(20, snapshot.TotalTurns);
        Assert.AreEqual(0.95, snapshot.TurnSuccessRate, 0.0001);
        Assert.IsTrue(snapshot.IsWithinSlo);
    }

    [TestMethod]
    public void RecordTurn_DieciochoDeVeinte_RompeElSlo()
    {
        var metrics = new BlsSloMetrics();
        foreach (var _ in Enumerable.Range(0, 18)) metrics.RecordTurn(true);
        foreach (var _ in Enumerable.Range(0, 2)) metrics.RecordTurn(false);

        Assert.IsFalse(metrics.Snapshot().IsWithinSlo);
    }

    [TestMethod]
    public void RecordToolRejected_ContabilizaRechazosSeguros()
    {
        var metrics = new BlsSloMetrics();
        metrics.RecordToolRejected();
        metrics.RecordToolRejected();
        metrics.RecordToolRejected();

        Assert.AreEqual(3, metrics.Snapshot().ToolRejections);
    }
}
