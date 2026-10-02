// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// ExcepciÃ³n base para errores del cliente NIM.
/// </summary>
public class BlsNimException : BlsException
{
    /// <summary>
    /// Body de la respuesta HTTP (truncado a 8 KB) para diagnÃ³stico.
    /// </summary>
    public string? ResponseBody { get; init; }

    public BlsNimException(string message, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        ResponseBody = Truncate(responseBody, 8192);
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max) + "â€¦ [truncado]";
    }
}

/// <summary>
/// 401 Unauthorized - API key invÃ¡lida o ausente.
/// </summary>
public sealed class BlsNimAuthException : BlsNimException
{
    public BlsNimAuthException(string message, string? responseBody = null)
        : base(message, responseBody) { }
}

/// <summary>
/// 429 Too Many Requests - rate limit agotado tras reintentos.
/// </summary>
public sealed class BlsNimRateLimitException : BlsNimException
{
    public BlsNimRateLimitException(string message, string? responseBody = null)
        : base(message, responseBody) { }
}

/// <summary>
/// 5xx Server Error - fallo del servidor NIM tras reintentos.
/// </summary>
public sealed class BlsNimServerException : BlsNimException
{
    public BlsNimServerException(string message, string? responseBody = null)
        : base(message, responseBody) { }
}

/// <summary>
/// Timeout por chunk (120 s sin datos).
/// </summary>
public sealed class BlsNimTimeoutException : BlsNimException
{
    public BlsNimTimeoutException(string message)
        : base(message) { }
}

/// <summary>
/// Error de protocolo SSE/JSON (malformado).
/// </summary>
public sealed class BlsNimProtocolException : BlsNimException
{
    public BlsNimProtocolException(string message, string? responseBody = null, Exception? inner = null)
        : base(message, responseBody, inner) { }
}

/// <summary>
/// Error en el bridge JSON-RPC.
/// </summary>
public sealed class BlsBridgeException : BlsException
{
    public BlsBridgeException(string message)
        : base(message) { }
}
