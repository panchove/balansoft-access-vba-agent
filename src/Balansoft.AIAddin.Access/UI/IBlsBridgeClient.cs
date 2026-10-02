// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Cliente del bridge JSON-RPC para comunicaciÃ³n con BlsVbeBridgeServer vÃ­a Named Pipe.
/// </summary>
public interface IBlsBridgeClient : IDisposable
{
    /// <summary>
    /// Invoca un mÃ©todo en el bridge vÃ­a Named Pipe.
    /// </summary>
    /// <typeparam name="TResult">Tipo del resultado esperado.</typeparam>
    /// <param name="method">Nombre del mÃ©todo JSON-RPC.</param>
    /// <param name="parameters">ParÃ¡metros del mÃ©todo (se serializan a JSON).</param>
    /// <param name="ct">Token de cancelaciÃ³n del turno (ADR-003).</param>
    /// <returns>Resultado deserializado.</returns>
    Task<TResult> InvokeAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken ct);
}
