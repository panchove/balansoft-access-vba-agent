// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Core;

/// <summary>
/// Contrato que ve <see cref="BalansoftAddin"/> para arrancar/parar el bridge JSON-RPC.
/// </summary>
public interface IBlsBridgeServer : IDisposable
{
    /// <summary>Indica si el servidor está escuchando conexiones.</summary>
    bool IsRunning { get; }

    /// <summary>Arranca el loop de aceptación. No bloquea.</summary>
    void Start();

    /// <summary>
    /// Para el servidor: cancela el loop, cierra el pipe y espera la finalización.
    /// </summary>
    /// <param name="timeout">Tiempo máximo de espera.</param>
    /// <returns>Task que completa cuando el servidor se ha detenido.</returns>
    Task StopAsync(TimeSpan timeout);
}
