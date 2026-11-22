// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IETF Agent Audit Trail / EU AI Act Art. 12 / ISO/IEC 27001 A.8.15
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Resultado de verificar la integridad de una cadena de auditoría.</summary>
/// <param name="IsValid"><c>true</c> si todos los eventos son íntegros.</param>
/// <param name="Count">Número de eventos verificados.</param>
/// <param name="FirstInvalidLine">Primera línea corrupta (1-based), o 0 si es válida.</param>
/// <param name="Reason">Descripción del fallo, o cadena vacía.</param>
/// <param name="LastHash">Hash del último evento válido; se usa para encadenar el día siguiente.</param>
public sealed record BlsAuditVerificationResult(
    bool IsValid,
    int Count,
    int FirstInvalidLine,
    string Reason,
    string? LastHash = null)
{
    /// <summary>Resultado de una verificación correcta.</summary>
    public static BlsAuditVerificationResult Ok(int count, string? lastHash = null)
        => new(true, count, 0, string.Empty, lastHash);

    /// <summary>Resultado de una verificación fallida.</summary>
    public static BlsAuditVerificationResult Fail(int line, string reason)
        => new(false, 0, line, reason);
}

/// <summary>
/// Cadena de hash SHA-256 enlazada conforme a IETF AAT: cada evento lleva
/// <c>prev_hash</c> (hash del evento anterior) y <c>hash</c> (SHA-256 de
/// <c>prev_hash + "|" + payload_json</c>). La verificación puede abarcar
/// varios ficheros diarios en orden cronológico, de modo que borrar un día
/// completo rompe la cadena (Fase 2, auditoría 2026-11-22).
/// </summary>
public static class BlsAuditChain
{
    /// <summary>Valor del <c>prev_hash</c> del primer evento de una cadena.</summary>
    public const string GenesisHash = "GENESIS";

    /// <summary>Prefijo de los ficheros de log diarios.</summary>
    public const string FilePrefix = "balansoft-";

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
    /// Devuelve <c>false</c> si el último evento no tiene un hash válido.
    /// </summary>
    public static bool TryReadLastHash(string path, out string? hash)
        => TryReadLastHash(path, out hash, out _);

    internal static bool TryReadLastHash(string path, out string? hash, out DateTime date)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(path, nameof(path));
        hash = null;
        date = DateTime.MinValue;

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
                if (!string.IsNullOrEmpty(hash) &&
                    doc.RootElement.TryGetProperty("ts", out var tsElement) &&
                    tsElement.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(tsElement.GetString(), out var parsed))
                {
                    date = parsed.Date;
                }
                return !string.IsNullOrEmpty(hash);
            }
        }
        catch (JsonException) { return false; }

        return false;
    }

    /// <summary>
    /// Verifica un fichero partiendo de GENESIS. Un fichero inexistente o
    /// vacío es válido.
    /// </summary>
    public static BlsAuditVerificationResult Verify(string path)
        => VerifyFrom(path, GenesisHash);

    /// <summary>
    /// Verifica todos los ficheros diarios de un directorio en orden
    /// cronológico, encadenando el último hash de cada día con el primero
    /// del siguiente. Detecta la manipulación o el borrado de un día entero.
    /// </summary>
    public static BlsAuditVerificationResult VerifyDirectory(string logDir)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(logDir, nameof(logDir));
        if (!Directory.Exists(logDir)) return BlsAuditVerificationResult.Ok(0);

        var files = Directory.GetFiles(logDir, FilePrefix + "*.log")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToList();

        var prev = GenesisHash;
        var total = 0;
        foreach (var file in files)
        {
            // Compatibilidad: los logs escritos antes de la Fase 2 empezaban
            // cada dia con GENESIS. Se recogn y se verifican como segmentos
            // independientes; los ficheros nuevos encadenan con el dia anterior.
            var effectivePrev = StartsNewChain(file) ? GenesisHash : prev;
            var result = VerifyFrom(file, effectivePrev);
            if (!result.IsValid)
            {
                return BlsAuditVerificationResult.Fail(
                    result.FirstInvalidLine,
                    $"{Path.GetFileName(file)}:{result.FirstInvalidLine}: {result.Reason}");
            }
            total += result.Count;
            prev = result.LastHash ?? prev;
        }

        return BlsAuditVerificationResult.Ok(total, prev);
    }

    /// <summary>
    /// Busca el último hash válido de los ficheros anteriores a la fecha
    /// indicada. Se usa como semilla del fichero del día actual para que la
    /// cadena no se reinicie cada día.
    /// </summary>
    public static string SeedFor(string logDir, DateTime date)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(logDir, nameof(logDir));
        if (!Directory.Exists(logDir)) return GenesisHash;

        var earlier = Directory.GetFiles(logDir, FilePrefix + "*.log")
            .Where(p => string.CompareOrdinal(Path.GetFileName(p), FileNameFor(date)) < 0)
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal);

        foreach (var file in earlier)
        {
            if (TryReadLastHash(file, out var hash, out _) && !string.IsNullOrEmpty(hash))
            {
                return hash!;
            }
        }

        return GenesisHash;
    }

    /// <summary>Nombre del fichero de log correspondiente a una fecha UTC.</summary>
    public static string FileNameFor(DateTime date)
        => $"{FilePrefix}{date:yyyyMMdd}.log";

    private static bool StartsNewChain(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var value = doc.RootElement.TryGetProperty("prev_hash", out var element)
                    ? element.GetString()
                    : null;
                return string.Equals(value, GenesisHash, StringComparison.Ordinal);
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
        return false;
    }

    private static BlsAuditVerificationResult VerifyFrom(string path, string initialPrev)
    {
        if (!File.Exists(path)) return BlsAuditVerificationResult.Ok(0, initialPrev);

        var prev = initialPrev;
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

        return BlsAuditVerificationResult.Ok(count, prev);
    }
}
