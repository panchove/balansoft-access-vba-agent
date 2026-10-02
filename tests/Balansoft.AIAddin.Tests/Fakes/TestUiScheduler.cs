// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Balansoft.AIAddin.UI;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// ImplementaciÃ³n de IUiScheduler para tests: ejecuta en el hilo actual.
/// Registra invocaciones para verificaciÃ³n.
/// </summary>
public sealed class TestUiScheduler : IUiScheduler
{
    public int RunCount { get; private set; }
    public int RunAsyncCount { get; private set; }
    public List<string> InvokedOperations { get; } = new();

    public T Run<T>(Func<T> op)
    {
        RunCount++;
        return op();
    }

    public async Task<T> RunAsync<T>(Func<T> op)
    {
        RunAsyncCount++;
        return await Task.FromResult(op()).ConfigureAwait(false);
    }

    public void Run(Action op)
    {
        RunCount++;
        op();
    }

    public async Task RunAsync(Func<Task> op)
    {
        RunAsyncCount++;
        await op().ConfigureAwait(false);
    }

    public void RecordInvocation(string operationName)
    {
        InvokedOperations.Add(operationName);
    }
}
