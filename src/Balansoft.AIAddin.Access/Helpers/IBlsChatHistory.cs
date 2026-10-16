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
    /// Lista los metadatos de las sesiones disponibles para un proyecto ordenadas por fecha reciente.
    /// </summary>
    Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(string projectKey, CancellationToken ct);

    /// <summary>
    /// Carga una sesión específica de un proyecto. Si sessionId es null o vacío, carga la más reciente o una nueva.
    /// </summary>
    Task<HistoryDocument> LoadSessionAsync(string projectKey, string? sessionId, CancellationToken ct);

    /// <summary>
    /// Crea una nueva sesión para el proyecto.
    /// </summary>
    Task<HistoryDocument> CreateSessionAsync(string projectKey, string? title, CancellationToken ct);

    /// <summary>
    /// Elimina una sesión específica del proyecto o todo el historial si sessionId es null.
    /// </summary>
    Task DeleteSessionAsync(string projectKey, string? sessionId, CancellationToken ct);
}
