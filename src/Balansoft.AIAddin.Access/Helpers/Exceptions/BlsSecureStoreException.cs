// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Excepción base para errores del Secure Store (DPAPI).
/// </summary>
public class BlsSecureStoreException : BlsException
{
    public BlsSecureStoreException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Excepción base para errores de Chat History.
/// </summary>
public class BlsHistoryException : BlsException
{
    public BlsHistoryException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Error de I/O al cargar/guardar historial.
/// </summary>
public class BlsHistoryIOException : BlsHistoryException
{
    public BlsHistoryIOException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Historial corrupto (JSON inválido o versión desconocida).
/// </summary>
public class BlsHistoryCorruptException : BlsHistoryException
{
    public BlsHistoryCorruptException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Versión de esquema de historial desconocida.
/// </summary>
public class BlsHistorySchemaException : BlsHistoryException
{
    public int Version { get; }

    public BlsHistorySchemaException(int version, string? message = null, Exception? inner = null)
        : base(message ?? $"Versión de historial desconocida: {version}", inner)
    {
        Version = version;
    }
}
