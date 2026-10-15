// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Proveedor de contexto VBE para inyectar en el system prompt.
/// </summary>
public interface IVbeContextProvider
{
    /// <summary>
    /// Obtiene el contexto actual del VBE.
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Contexto VBE con módulo activo y selección.</returns>
    Task<VbeContext> GetAsync(CancellationToken ct);
}

/// <summary>
/// Contexto VBE para inyectar en el system prompt.
/// </summary>
public sealed record VbeContext(
    string? ActiveModuleName,
    string? ActiveModuleType,
    string? SelectionText,
    bool SelectionTruncated);
