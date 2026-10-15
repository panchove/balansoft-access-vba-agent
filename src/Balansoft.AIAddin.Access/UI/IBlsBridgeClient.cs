// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Cliente del bridge JSON-RPC para comunicación con BlsVbeBridgeServer vía Named Pipe.
/// </summary>
public interface IBlsBridgeClient : IDisposable
{
    /// <summary>
    /// Invoca un método en el bridge vía Named Pipe.
    /// </summary>
    /// <typeparam name="TResult">Tipo del resultado esperado.</typeparam>
    /// <param name="method">Nombre del método JSON-RPC.</param>
    /// <param name="parameters">Parámetros del método (se serializan a JSON).</param>
    /// <param name="ct">Token de cancelación del turno (ADR-003).</param>
    /// <returns>Resultado deserializado.</returns>
    Task<TResult> InvokeAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken ct);
}
