// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Reflection;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Version de ejecucion del add-in, inyectada por Nerdbank.GitVersioning.
    /// Se usa en la barra de titulo de la terminal y en el dialogo de ajustes.
    /// </summary>
    public static class BlsVersion
    {
        public static string Current
        {
            get
            {
                var assembly = typeof(BlsVersion).Assembly;
                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    var plus = informational!.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational!;
                }
                return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }
        }
    }
}
