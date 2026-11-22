// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 27001:2022 A.8.15 / ISO/IEC 27701 / Pilar 24 (privacidad)
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;
using System.Security.Cryptography;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Solicitud de aprobación humana para una tool call. Incluye el hash SHA-256 del payload,
/// de modo que el log de auditoría pueda vincular la decisión con los argumentos exactos
/// sin escribir datos potencialmente personales en claro (Pilar 24).
/// </summary>
public sealed class BlsApprovalRequest
{
    private const int PreviewLength = 320;

    /// <summary>Nombre de la tool solicitada.</summary>
    public string ToolName { get; }

    /// <summary>Riesgo calculado por <see cref="BlsToolRiskPolicy"/>.</summary>
    public BlsToolRisk Risk { get; }

    /// <summary>JSON original de argumentos (solo para mostrarlo al humano; nunca se loguea).</summary>
    public string PayloadJson { get; }

    /// <summary>Hex SHA-256 en mayúsculas del payload UTF-8.</summary>
    public string PayloadSha256 { get; }

    /// <summary>Vista previa truncada del payload para el diálogo de aprobación.</summary>
    public string Preview { get; }

    private BlsApprovalRequest(
        string toolName,
        BlsToolRisk risk,
        string payloadJson,
        string payloadSha256,
        string preview)
    {
        ToolName = toolName;
        Risk = risk;
        PayloadJson = payloadJson;
        PayloadSha256 = payloadSha256;
        Preview = preview;
    }

    /// <summary>Crea una solicitud y calcula su huella criptográfica.</summary>
    public static BlsApprovalRequest Create(string toolName, BlsToolRisk risk, string? payloadJson)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(toolName, nameof(toolName));

        var payload = payloadJson ?? "{}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty);

        var preview = payload.Length <= PreviewLength
            ? payload
            : payload.Substring(0, PreviewLength) + "…";

        return new BlsApprovalRequest(toolName, risk, payload, hex, preview);
    }
}
