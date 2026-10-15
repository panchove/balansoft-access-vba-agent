// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// Logger fake que captura mensajes en memoria para verificación en tests.
/// </summary>
public sealed class FakeLogger : IBlsLogger
{
    public sealed record LogEntry(string Level, string Component, string Message, Exception? Exception);

    public List<LogEntry> Entries { get; } = new();
    public object Lock { get; } = new();

    public void Debug(string component, string message)
    {
        lock (Lock)
        {
            Entries.Add(new LogEntry("DEBUG", component, message, null));
        }
    }

    public void Info(string component, string message)
    {
        lock (Lock)
        {
            Entries.Add(new LogEntry("INFO", component, message, null));
        }
    }

    public void Warn(string component, string message, Exception? ex = null)
    {
        lock (Lock)
        {
            Entries.Add(new LogEntry("WARN", component, message, ex));
        }
    }

    public void Error(string component, string message, Exception? ex = null)
    {
        lock (Lock)
        {
            Entries.Add(new LogEntry("ERROR", component, message, ex));
        }
    }

    public bool ContainsApiKey(string apiKey)
    {
        lock (Lock)
        {
            return Entries.Any(e => e.Message.Contains(apiKey) ||
                                   (e.Exception?.Message?.Contains(apiKey) ?? false) ||
                                   (e.Exception?.ToString()?.Contains(apiKey) ?? false));
        }
    }

    public void Clear()
    {
        lock (Lock)
        {
            Entries.Clear();
        }
    }
}
