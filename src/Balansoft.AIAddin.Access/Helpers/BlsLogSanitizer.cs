// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 27001 A.8.24 / ISO/IEC 27701 / OWASP ASVS / Pilar 24
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Text.RegularExpressions;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Detalle seguro de una excepción, listo para serializar en el log.</summary>
/// <param name="Type">Nombre del tipo de excepción.</param>
/// <param name="Message">Mensaje saneado (sin secretos ni saltos de línea).</param>
/// <param name="HResult">HResult en hexadecimal para <see cref="System.Runtime.InteropServices.COMException"/>, si aplica.</param>
public sealed record BlsExceptionDetail(string Type, string Message, string? HResult);

/// <summary>
/// Redacción determinista de credenciales antes de escribir cualquier evento
/// de auditoría. F3 revierte la decisión de ADR-011 §11.12 ("el logger no
/// sabe qué es una API key"): con el registro append-only exigido por el
/// Pilar 6, la última barrera antes del disco DEBE estar en el logger.
/// </summary>
public static class BlsLogSanitizer
{
    /// <summary>Longitud máxima del mensaje saneado.</summary>
    public const int MaxMessageLength = 4000;

    private const string TruncationSuffix = "…[TRUNCATED]";

    private static readonly Regex BearerToken = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.Compiled);

    private static readonly Regex NvidiaKey = new(
        @"(?i)\bnvapi-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled);

    private static readonly Regex GenericKey = new(
        @"(?i)\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled);

    // El prefijo opcional permite redactar claves con scope (NIM_API_KEY,
    // AZURE_OPENAI_API_KEY, etc.) conservando el nombre para diagnosticar.
    private static readonly Regex NamedSecret = new(
        @"(?i)\b((?:[A-Za-z0-9]+[_-])?(?:api[_-]?key|apikey|authorization|access[_-]?token|refresh[_-]?token|token|secret|password|pwd))\b\s*[:=]\s*[""']?[^\s,;""'}\]]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Sanea un mensaje: elimina saltos de línea, redacta credenciales y trunca.
    /// Nunca devuelve <c>null</c>.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var value = text.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        // Orden importante: primero los tokens con prefijo, después pares clave=valor.
        value = BearerToken.Replace(value, "Bearer [REDACTED]");
        value = NvidiaKey.Replace(value, "[REDACTED]");
        value = GenericKey.Replace(value, "[REDACTED]");
        value = NamedSecret.Replace(value, "$1=[REDACTED]");

        if (value.Length <= MaxMessageLength) return value;
        return value.Substring(0, MaxMessageLength - TruncationSuffix.Length) + TruncationSuffix;
    }

    /// <summary>
    /// Convierte una excepción en un detalle seguro. No se incluye el stack trace:
    /// puede contener rutas de usuario y es PII potencial (Pilar 24).
    /// </summary>
    public static BlsExceptionDetail DescribeException(Exception ex)
    {
        BlsContract.RequiresNotNull(ex, nameof(ex));

        string? hresult = null;
        if (ex is System.Runtime.InteropServices.COMException)
        {
            hresult = ex.HResult.ToString("X8");
        }

        return new BlsExceptionDetail(
            ex.GetType().Name,
            Sanitize(ex.Message),
            hresult);
    }
}
