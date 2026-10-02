// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Core;

/// <summary>
/// Contrato que ve <see cref="BalansoftAddin"/> para arrancar/parar el bridge JSON-RPC.
/// </summary>
public interface IBlsBridgeServer : IDisposable
{
    /// <summary>Indica si el servidor estÃ¡ escuchando conexiones.</summary>
    bool IsRunning { get; }

    /// <summary>Arranca el loop de aceptaciÃ³n. No bloquea.</summary>
    void Start();

    /// <summary>
    /// Para el servidor: cancela el loop, cierra el pipe y espera la finalizaciÃ³n.
    /// </summary>
    /// <param name="timeout">Tiempo mÃ¡ximo de espera.</param>
    /// <returns>Task que completa cuando el servidor se ha detenido.</returns>
    Task StopAsync(TimeSpan timeout);
}
