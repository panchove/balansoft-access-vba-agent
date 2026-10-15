// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Balansoft.AIAddin.UI
{
    /// <summary>
    /// Programador de operaciones en el hilo UI usando Control.Invoke de WinForms.
    /// </summary>
    public sealed class WinFormsUiScheduler : IUiScheduler
    {
        private readonly Control? _invoker;

        public WinFormsUiScheduler(Control? invoker = null)
        {
            _invoker = invoker;
        }

        public T Run<T>(Func<T> op)
        {
            if (_invoker != null && _invoker.InvokeRequired)
            {
                return (T)_invoker.Invoke(op);
            }
            return op();
        }

        public Task<T> RunAsync<T>(Func<T> op)
        {
            if (_invoker != null && _invoker.InvokeRequired)
            {
                var tcs = new TaskCompletionSource<T>();
                _invoker.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        tcs.SetResult(op());
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
                return tcs.Task;
            }

            return Task.FromResult(op());
        }

        public void Run(Action op)
        {
            if (_invoker != null && _invoker.InvokeRequired)
            {
                _invoker.Invoke(op);
                return;
            }
            op();
        }

        public Task RunAsync(Func<Task> op)
        {
            if (_invoker != null && _invoker.InvokeRequired)
            {
                var tcs = new TaskCompletionSource<bool>();
                _invoker.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await op().ConfigureAwait(false);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
                return tcs.Task;
            }

            return op();
        }
    }
}
