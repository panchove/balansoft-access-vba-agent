// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: EU AI Act Art. 14 / ISO/IEC 42001 / OWASP Agentic ASI01
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Resultado de evaluar una solicitud de aprobación.</summary>
public enum ApprovalDecision
{
    /// <summary>La tool es de solo lectura; no se pidió aprobación.</summary>
    NotRequired,

    /// <summary>El humano aprobó la ejecución.</summary>
    Approved,

    /// <summary>El humano denegó, o el prompt falló (fail-safe: denegar).</summary>
    Denied
}

/// <summary>
/// Gate humano (Human-in-the-Loop). Toda tool de riesgo medio o alto debe pasar por aquí.
/// El prompt es un delegate para que la lógica sea testeable sin WinForms; la terminal
/// inyecta un MessageBox con el botón "No" por defecto.
/// </summary>
public sealed class BlsHumanApprovalGate
{
    private readonly Func<BlsApprovalRequest, bool> _prompt;
    private readonly IBlsLogger _logger;

    public BlsHumanApprovalGate(Func<BlsApprovalRequest, bool> prompt, IBlsLogger logger)
    {
        _prompt = BlsContract.RequiresNotNull(prompt, nameof(prompt));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
    }

    /// <summary>
    /// Evalúa la solicitud. Ante cualquier fallo del prompt se deniega (fail-safe) y se registra.
    /// </summary>
    public ApprovalDecision Evaluate(BlsApprovalRequest request)
    {
        BlsContract.RequiresNotNull(request, nameof(request));

        if (!BlsToolRiskPolicy.RequiresApproval(request.Risk))
        {
            _logger.Debug("HITL", $"Tool {request.ToolName} (riesgo {request.Risk}) no requiere aprobación.");
            return ApprovalDecision.NotRequired;
        }

        bool approved;
        try
        {
            approved = _prompt(request);
        }
        catch (Exception ex)
        {
            // Fail-safe: si no podemos preguntar, no ejecutamos.
            _logger.Error(
                "HITL",
                $"Fallo solicitando aprobación de {request.ToolName} (payload_sha256={request.PayloadSha256})",
                ex);
            return ApprovalDecision.Denied;
        }

        var decision = approved ? ApprovalDecision.Approved : ApprovalDecision.Denied;
        _logger.Info(
            "HITL",
            $"Decisión {decision} para {request.ToolName} (riesgo {request.Risk}, payload_sha256={request.PayloadSha256})");

        return decision;
    }
}
