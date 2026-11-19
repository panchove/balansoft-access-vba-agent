// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Única fuente de verdad del identificador de modelo (R-07 / Pilar 10).
/// El valor por defecto está compilado: nunca se descarga en ejecución.
/// </summary>
public static class BlsModelCatalog
{
    /// <summary>Modelo de producción, fijado en el AI-BOM.</summary>
    public const string DefaultModel = "nvidia/nemotron-3.5-lightning-30b-a3b";

    /// <summary>Variable de entorno que permite el override del modelo.</summary>
    public const string EnvironmentVariable = "NIM_MODEL";

    /// <summary>
    /// Resuelve el modelo con prioridad parámetro &gt; entorno &gt; default.
    /// Valores vacíos o whitespace se tratan como "no configurado".
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return BlsContract.RequiresNotNullOrWhiteSpace(configured, nameof(configured));

        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(env)
            ? DefaultModel
            : BlsContract.RequiresNotNullOrWhiteSpace(env, nameof(env));
    }
}
