// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

/// <summary>F5 / R-07 — fuente única del modelo, verificable en AI-BOM.</summary>
[TestClass]
public class BlsModelCatalogTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, null);
    }

    [TestMethod]
    public void DefaultModel_NimHttpRequestBuilder_EsAliasDelCatalogo()
    {
        Assert.AreEqual(BlsModelCatalog.DefaultModel, NimHttpRequestBuilder.DefaultModel);
        Assert.AreEqual("nvidia/nemotron-3.5-lightning-30b-a3b", BlsModelCatalog.DefaultModel);
    }

    [TestMethod]
    public void Resolve_Configurado_GanaSobreElEntorno()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, "modelo-env");

        Assert.AreEqual("modelo-parametro", BlsModelCatalog.Resolve("modelo-parametro"));
    }

    [TestMethod]
    public void Resolve_SoloEntorno_DevuelveElOverride()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, "modelo-env");

        Assert.AreEqual("modelo-env", BlsModelCatalog.Resolve(null));
    }

    [TestMethod]
    public void Resolve_SinConfiguracion_DevuelveElDefaultDelCatalogo()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, null);

        Assert.AreEqual(BlsModelCatalog.DefaultModel, BlsModelCatalog.Resolve(null));
    }

    [TestMethod]
    public void Resolve_Whitespace_CaeAlEntornoYDespuesAlDefault()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, null);
        Assert.AreEqual(BlsModelCatalog.DefaultModel, BlsModelCatalog.Resolve("   "));

        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, "modelo-env");
        Assert.AreEqual("modelo-env", BlsModelCatalog.Resolve("   "));
    }

    [TestMethod]
    public void ResolveModel_DelegaEnElCatalogoConWhitespace()
    {
        Environment.SetEnvironmentVariable(BlsModelCatalog.EnvironmentVariable, null);

        Assert.AreEqual(BlsModelCatalog.DefaultModel, NimHttpRequestBuilder.ResolveModel(string.Empty));
    }
}
