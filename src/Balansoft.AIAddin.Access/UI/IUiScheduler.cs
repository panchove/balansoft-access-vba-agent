// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Programador de operaciones en el hilo UI del host (Access/VBE).
/// Implementación real: WinFormsUiScheduler (usa Control.Invoke).
/// Implementación de test: TestUiScheduler (ejecuta en hilo actual).
/// </summary>
public interface IUiScheduler
{
    /// <summary>Ejecuta la operación en el hilo UI (bloqueante).</summary>
    T Run<T>(Func<T> op);

    /// <summary>Ejecuta la operación en el hilo UI (asíncrono).</summary>
    Task<T> RunAsync<T>(Func<T> op);

    /// <summary>Ejecuta una acción en el hilo UI (bloqueante).</summary>
    void Run(Action op);

    /// <summary>Ejecuta una acción en el hilo UI (asíncrono).</summary>
    Task RunAsync(Func<Task> op);
}
