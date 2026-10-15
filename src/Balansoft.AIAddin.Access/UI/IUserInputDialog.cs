// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Windows.Forms;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Abstracción para diálogos de entrada de usuario (testeable).
/// </summary>
public interface IUserInputDialog
{
    /// <summary>
    /// Solicita la API key al usuario mediante un diálogo modal.
    /// </summary>
    /// <param name="owner">Ventana propietaria.</param>
    /// <returns>API key ingresada o null si se canceló.</param>
    string? PromptForApiKey(IWin32Window owner);
}
