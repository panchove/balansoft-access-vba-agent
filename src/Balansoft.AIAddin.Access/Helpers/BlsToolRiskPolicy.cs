// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 42001:2023 / OWASP Agentic ASI02 / NIST AI 600-1
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;
using System.Collections.Generic;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Nivel de riesgo de una tool exposed al modelo.</summary>
public enum BlsToolRisk
{
    /// <summary>Solo lectura; no requiere aprobación.</summary>
    Low,

    /// <summary>Escribe pero no destruye; requiere aprobación.</summary>
    Medium,

    /// <summary>Destructiva o migratoria; requiere aprobación y kill switch accesible.</summary>
    High
}

/// <summary>
/// Clasificación determinista de las tools de la whitelist (<c>JsonRpcMethods.All</c>).
/// Cualquier tool desconocida se clasifica como <see cref="BlsToolRisk.High"/> (fail-safe):
/// nunca se ejecuta una acción no inventariada sin aprobación humana.
/// </summary>
public sealed class BlsToolRiskPolicy
{
    private static readonly IReadOnlyDictionary<string, BlsToolRisk> Risks =
        new Dictionary<string, BlsToolRisk>(StringComparer.Ordinal)
        {
            // Lectura (sin efectos en el proyecto)
            ["list_modules"] = BlsToolRisk.Low,
            ["read_module"] = BlsToolRisk.Low,
            ["get_selection"] = BlsToolRisk.Low,
            ["get_active_module"] = BlsToolRisk.Low,
            ["project_summary"] = BlsToolRisk.Low,
            ["list_objects"] = BlsToolRisk.Low,
            ["object_detail"] = BlsToolRisk.Low,
            ["list_commandbars"] = BlsToolRisk.Low,
            ["inspect_adp"] = BlsToolRisk.Low,

            // Escritura no destructiva
            ["write_module"] = BlsToolRisk.Medium,
            ["create_module"] = BlsToolRisk.Medium,
            ["create_form"] = BlsToolRisk.Medium,
            ["create_table"] = BlsToolRisk.Medium,
            ["create_query"] = BlsToolRisk.Medium,
            ["create_ribbon"] = BlsToolRisk.Medium,

            // Destructivas / migratorias
            ["delete_module"] = BlsToolRisk.High,
            ["execute_sql"] = BlsToolRisk.High,
            ["migrate_commandbar_to_ribbon"] = BlsToolRisk.High,
            ["migrate_adp_to_accdb"] = BlsToolRisk.High
        };

    /// <summary>Devuelve el riesgo de una tool. Desconocida ⇒ <see cref="BlsToolRisk.High"/>.</summary>
    public BlsToolRisk GetRisk(string toolName)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(toolName, nameof(toolName));
        return Risks.TryGetValue(toolName, out var risk) ? risk : BlsToolRisk.High;
    }

    /// <summary>Indica si un nivel de riesgo exige aprobación humana.</summary>
    public static bool RequiresApproval(BlsToolRisk risk) => risk != BlsToolRisk.Low;
}
