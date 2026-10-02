// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Windows.Forms;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// ImplementaciÃ³n WinForms de IUserInputDialog para el prompt de API key.
/// </summary>
internal sealed class WinFormsUserInputDialog : IUserInputDialog
{
    private readonly IBlsLogger _logger;

    public WinFormsUserInputDialog(IBlsLogger logger)
    {
        _logger = logger;
    }

    public string? PromptForApiKey(IWin32Window owner)
    {
        using var form = new ApiKeyPromptForm();
        var result = form.ShowDialog(owner);
        return result == DialogResult.OK ? form.ApiKey : null;
    }
}
