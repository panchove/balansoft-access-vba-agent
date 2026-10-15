// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Contrato de persistencia del historial de chat por proyecto.
/// </summary>
public interface IBlsChatHistory
{
    /// <summary>
    /// Carga el historial de un proyecto.
    /// </summary>
    /// <param name="projectKey">Clave del proyecto (ADR-001).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Documento de historial (nuevo si no existe).</returns>
    Task<HistoryDocument> LoadAsync(string projectKey, CancellationToken ct);

    /// <summary>
    /// Guarda el historial de un proyecto (con debounce).
    /// </summary>
    /// <param name="projectKey">Clave del proyecto.</param>
    /// <param name="doc">Documento a guardar.</param>
    /// <param name="ct">Token de cancelación.</param>
    Task SaveAsync(string projectKey, HistoryDocument doc, CancellationToken ct);

    /// <summary>
    /// Fuerza la escritura de cualquier guardado pendiente.
    /// </summary>
    Task FlushAsync(CancellationToken ct);

    /// <summary>
    /// Elimina el historial de un proyecto.
    /// </summary>
    Task ClearAsync(string projectKey, CancellationToken ct);
}
