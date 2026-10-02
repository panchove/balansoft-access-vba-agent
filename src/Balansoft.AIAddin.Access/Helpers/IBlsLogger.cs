// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Logger del add-in. Escrito a archivo diario en %APPDATA%\Balansoft\AIAddin\logs\.
/// </summary>
public interface IBlsLogger
{
    void Debug(string component, string message);
    void Info(string component, string message);
    void Warn(string component, string message, Exception? ex = null);
    void Error(string component, string message, Exception? ex = null);
}
