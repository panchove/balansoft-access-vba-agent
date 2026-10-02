// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Windows.Forms;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// AbstracciÃ³n para diÃ¡logos de entrada de usuario (testeable).
/// </summary>
public interface IUserInputDialog
{
    /// <summary>
    /// Solicita la API key al usuario mediante un diÃ¡logo modal.
    /// </summary>
    /// <param name="owner">Ventana propietaria.</param>
    /// <returns>API key ingresada o null si se cancelÃ³.</param>
    string? PromptForApiKey(IWin32Window owner);
}
