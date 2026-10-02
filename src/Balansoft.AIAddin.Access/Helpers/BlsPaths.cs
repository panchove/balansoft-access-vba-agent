// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Centraliza las rutas del perfil del add-in.
/// </summary>
internal static class BlsPaths
{
    private static readonly string RootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Balansoft", "AIAddin");

    public static string Root => RootPath;
    public static string ApiKeyFile => Path.Combine(RootPath, "apikey.bin");
    public static string HistoryDir => Path.Combine(RootPath, "history");
    public static string LogsDir => Path.Combine(RootPath, "logs");
}
