// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Collections.Generic;
using System.Linq;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

/// <summary>F5 / R-03 — consentimiento explícito antes de enviar datos a NIM.</summary>
[TestClass]
public class BlsPiiConsentGateTests
{
    private const string OriginalEmail = "juan.perez@ejemplo.com";

    private static IReadOnlyList<BlsPiiFinding> Findings(params string[] categories)
        => categories.Select(c => new BlsPiiFinding(c, "[" + c + "]")).ToList();

    [TestMethod]
    public void SinHallazgos_NoLlamaAlPromptYAprueba()
    {
        var calls = 0;
        var gate = new BlsPiiConsentGate(_ => { calls++; return false; }, new FakeLogger());

        var approved = gate.Approve(Array.Empty<BlsPiiFinding>(), "sin datos");

        Assert.IsTrue(approved);
        Assert.AreEqual(0, calls, "Sin hallazgos no se molesta al usuario.");
        Assert.IsFalse(BlsPiiConsentGate.IsRequired(Array.Empty<BlsPiiFinding>()));
    }

    [TestMethod]
    public void ConHallazgos_Aprobado_LlamaUnaVezYDevuelveTrue()
    {
        var calls = 0;
        var gate = new BlsPiiConsentGate(request => { calls++; return true; }, new FakeLogger());

        var approved = gate.Approve(Findings("EMAIL"), "[EMAIL]");

        Assert.IsTrue(approved);
        Assert.AreEqual(1, calls, "Una aprobación ⇒ una única petición HTTP.");
    }

    [TestMethod]
    public void ConHallazgos_Rechazado_DevuelveFalse()
    {
        var gate = new BlsPiiConsentGate(_ => false, new FakeLogger());

        var approved = gate.Approve(Findings("EMAIL"), "[EMAIL]");

        Assert.IsFalse(approved,
            "Un rechazo aborta el turno: nunca éxito falso (Pilar 8).");
    }

    [TestMethod]
    public void Request_Categorias_DeduplicadasYOrdenadas()
    {
        BlsPiiConsentRequest? captured = null;
        var gate = new BlsPiiConsentGate(request => { captured = request; return true; }, new FakeLogger());

        gate.Approve(Findings("PHONE", "EMAIL", "PHONE", "EMAIL"), "[EMAIL] [PHONE]");

        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(new[] { "EMAIL", "PHONE" }, captured!.Categories.ToList());
    }

    [TestMethod]
    public void Request_VistaPrevia_SoloContieneTokensRedactados()
    {
        BlsPiiConsentRequest? captured = null;
        var gate = new BlsPiiConsentGate(request => { captured = request; return true; }, new FakeLogger());

        gate.Approve(Findings("EMAIL"), "Escribe a [EMAIL]");

        Assert.IsNotNull(captured);
        StringAssert.Contains(captured!.RedactedPreview, "[EMAIL]");
        Assert.IsFalse(captured.RedactedPreview.Contains(OriginalEmail));
    }

    [TestMethod]
    public void Log_SoloRegistraCategorias_NuncaValoresOriginales()
    {
        var logger = new FakeLogger();
        var gate = new BlsPiiConsentGate(_ => false, logger);

        gate.Approve(Findings("EMAIL", "CONNECTION_STRING"), OriginalEmail);

        Assert.IsTrue(logger.Entries.Any(e =>
            e.Component == "BlsPiiConsentGate" && e.Message.Contains("EMAIL")));
        Assert.IsFalse(logger.ContainsApiKey(OriginalEmail));
    }
}
