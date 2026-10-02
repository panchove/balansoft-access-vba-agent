// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Tests.Infrastructure;

/// <summary>
/// Utilidades de test compatibles con .NET Framework 4.8.
/// <c>Task.WaitAsync</c> solo existe en .NET 6+, por lo que aquÃ­ se
/// implementa con <see cref="Task.WhenAny(Task, Task)"/>.
/// </summary>
public static class TaskTestExtensions
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Espera a que la tarea termine; lanza <see cref="TimeoutException"/> si no.
    /// </summary>
    public static async Task WithTimeout(this Task task, TimeSpan timeout)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);

        if (!ReferenceEquals(finished, task))
            throw new TimeoutException($"La tarea no terminÃ³ en {timeout.TotalSeconds:F1}s");

        await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Espera a la tarea y devuelve su resultado; lanza <see cref="TimeoutException"/> si no.
    /// </summary>
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        await task.WithTimeout(timeout).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
}

