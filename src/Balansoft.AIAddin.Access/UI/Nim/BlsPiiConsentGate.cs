// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Collections.Generic;
using System.Linq;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>Solicitud de consentimiento: categorías y vista previa saneada.</summary>
public sealed record BlsPiiConsentRequest(
    IReadOnlyList<string> Categories,
    string RedactedPreview);

/// <summary>
/// Puerta de consentimiento para el envío de datos a NIM (R-03). El delegado
/// de prompt se inyecta para poder verificar el rechazo sin WinForms.
/// </summary>
public sealed class BlsPiiConsentGate
{
    private readonly Func<BlsPiiConsentRequest, bool> _prompt;
    private readonly IBlsLogger _logger;

    public BlsPiiConsentGate(Func<BlsPiiConsentRequest, bool> prompt, IBlsLogger logger)
    {
        _prompt = BlsContract.RequiresNotNull(prompt, nameof(prompt));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
    }

    public static bool IsRequired(IReadOnlyList<BlsPiiFinding>? findings)
        => findings != null && findings.Count > 0;

    /// <summary>
    /// Devuelve true si no hay hallazgos o si el usuario aprobó explícitamente.
    /// Un rechazo NUNCA se reporta como éxito (Pilar 8).
    /// </summary>
    public bool Approve(IReadOnlyList<BlsPiiFinding>? findings, string? redactedPreview)
    {
        if (!IsRequired(findings))
            return true;

        var categories = findings!
            .Select(f => f.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var approved = _prompt(new BlsPiiConsentRequest(categories, redactedPreview ?? string.Empty));

        // Solo se registran categorías: ningún valor original llega al log.
        _logger.Info(
            "BlsPiiConsentGate",
            $"consent categories={string.Join(",", categories)} approved={approved}");

        return approved;
    }
}
