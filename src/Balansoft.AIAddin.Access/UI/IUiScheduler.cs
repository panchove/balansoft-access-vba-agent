// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Programador de operaciones en el hilo UI del host (Access/VBE).
/// ImplementaciÃ³n real: WinFormsUiScheduler (usa Control.Invoke).
/// ImplementaciÃ³n de test: TestUiScheduler (ejecuta en hilo actual).
/// </summary>
public interface IUiScheduler
{
    /// <summary>Ejecuta la operaciÃ³n en el hilo UI (bloqueante).</summary>
    T Run<T>(Func<T> op);

    /// <summary>Ejecuta la operaciÃ³n en el hilo UI (asÃ­ncrono).</summary>
    Task<T> RunAsync<T>(Func<T> op);

    /// <summary>Ejecuta una acciÃ³n en el hilo UI (bloqueante).</summary>
    void Run(Action op);

    /// <summary>Ejecuta una acciÃ³n en el hilo UI (asÃ­ncrono).</summary>
    Task RunAsync(Func<Task> op);
}
