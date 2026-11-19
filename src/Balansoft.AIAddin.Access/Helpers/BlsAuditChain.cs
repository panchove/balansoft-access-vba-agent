// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IETF Agent Audit Trail / EU AI Act Art. 12 / ISO/IEC 27001 A.8.15
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Resultado de verificar la integridad de una cadena de auditoría.</summary>
/// <param name="IsValid"><c>true</c> si todos los eventos son íntegros.</param>
/// <param name="Count">Número de eventos verificados.</param>
/// <param name="FirstInvalidLine">Primera línea corrupta (1-based), o 0 si es válida.</param>
/// <param name="Reason">Descripción del fallo, o cadena vacía.</param>
public sealed record BlsAuditVerificationResult(
    bool IsValid,
    int Count,
    int FirstInvalidLine,
    string Reason)
{
    /// <summary>Resultado de una verificación correcta.</summary>
    public static BlsAuditVerificationResult Ok(int count) => new(true, count, 0, string.Empty);

    /// <summary>Resultado de una verificación fallida.</summary>
    public static BlsAuditVerificationResult Fail(int line, string reason) => new(false, 0, line, reason);
}

/// <summary>
/// Cadena de hash SHA-256 enlazada conforme a IETF AAT: cada evento lleva
/// <c>prev_hash</c> (hash del evento anterior) y <c>hash</c> (SHA-256 de
/// <c>prev_hash + "|" + payload_json</c>). El fichero es append-only; la
/// primera línea de cada día usa <see cref="GenesisHash"/> como origen.
/// </summary>
public static class BlsAuditChain
{
    /// <summary>Valor del <c>prev_hash</c> del primer evento de una cadena.</summary>
    public const string GenesisHash = "GENESIS";

    private const string HashField = ",\"hash\":\"";

    /// <summary>Calcula el hash de un evento a partir del hash anterior y su payload.</summary>
    public static string ComputeHash(string prevHash, string payloadJson)
    {
        BlsContract.RequiresNotNull(prevHash, nameof(prevHash));
        BlsContract.RequiresNotNull(payloadJson, nameof(payloadJson));

        var material = Encoding.UTF8.GetBytes(prevHash + "|" + payloadJson);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(material)).Replace("-", string.Empty);
    }

    /// <summary>
    /// Lee el hash del último evento de un fichero. Devuelve <c>true</c> con
    /// <c>hash = null</c> si el fichero no existe o está vacío (origen GENESIS).
    /// Devuelve <c>false</c> si el último evento no tiene un hash válido: en ese
    /// caso el logger NO debe escribir, para no continuar una cadena rota.
    /// </summary>
    public static bool TryReadLastHash(string path, out string? hash)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(path, nameof(path));
        hash = null;

        if (!File.Exists(path)) return true;

        string? last = null;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        if (last == null) return true;

        try
        {
            using var doc = JsonDocument.Parse(last);
            if (doc.RootElement.TryGetProperty("hash", out var element) &&
                element.ValueKind == JsonValueKind.String)
            {
                hash = element.GetString();
                return !string.IsNullOrEmpty(hash);
            }
        }
        catch (JsonException) { return false; }

        return false;
    }

    /// <summary>
    /// Verifica una cadena completa: para cada evento comprueba que
    /// <c>prev_hash</c> coincide con el hash anterior y que <c>hash</c> es
    /// SHA-256(prev_hash + payload). Un fichero inexistente o vacío es válido.
    /// </summary>
    public static BlsAuditVerificationResult Verify(string path)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(path, nameof(path));

        if (!File.Exists(path)) return BlsAuditVerificationResult.Ok(0);

        var prev = GenesisHash;
        var lineNumber = 0;
        var count = 0;

        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(rawLine)) continue;

                var line = rawLine.TrimEnd('\r', '\n');
                var index = line.LastIndexOf(HashField, StringComparison.Ordinal);
                if (index < 0 || !line.EndsWith("\"}", StringComparison.Ordinal))
                {
                    return BlsAuditVerificationResult.Fail(lineNumber, "Evento sin campo hash final.");
                }

                var payload = line.Substring(0, index) + "}";
                string hash;
                string? prevInLine;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    hash = root.GetProperty("hash").GetString() ?? string.Empty;
                    prevInLine = root.GetProperty("prev_hash").GetString();
                }
                catch (JsonException ex)
                {
                    return BlsAuditVerificationResult.Fail(lineNumber, $"JSON inválido: {ex.Message}");
                }
                catch (KeyNotFoundException)
                {
                    return BlsAuditVerificationResult.Fail(lineNumber, "Faltan campos hash/prev_hash.");
                }
                catch (InvalidOperationException)
                {
                    return BlsAuditVerificationResult.Fail(lineNumber, "Faltan campos hash/prev_hash.");
                }

                if (!string.Equals(prevInLine, prev, StringComparison.Ordinal))
                {
                    return BlsAuditVerificationResult.Fail(
                        lineNumber,
                        $"prev_hash esperado '{prev}' pero encontrado '{prevInLine}'.");
                }

                var expected = ComputeHash(prev, payload);
                if (!string.Equals(expected, hash, StringComparison.Ordinal))
                {
                    return BlsAuditVerificationResult.Fail(lineNumber, "Hash no coincide con el payload.");
                }

                prev = hash;
                count++;
            }
        }
        catch (IOException ex)
        {
            return BlsAuditVerificationResult.Fail(lineNumber + 1, $"No se pudo leer el log: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return BlsAuditVerificationResult.Fail(lineNumber + 1, $"Acceso denegado: {ex.Message}");
        }

        return BlsAuditVerificationResult.Ok(count);
    }
}
