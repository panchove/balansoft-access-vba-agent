// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Proveedor de contexto VBE que obtiene informaciÃ³n del mÃ³dulo activo y selecciÃ³n.
/// </summary>
internal sealed class VbeContextProvider : IVbeContextProvider
{
    private const int MaxSelectionLength = 32 * 1024; // 32 KB

    private readonly IBlsVbe _vbe;
    private readonly IUiScheduler _ui;
    private readonly IBlsLogger _logger;

    public VbeContextProvider(
        IBlsVbe vbe,
        IUiScheduler ui,
        IBlsLogger logger)
    {
        _vbe = vbe;
        _ui = ui;
        _logger = logger;
    }

    public async Task<VbeContext> GetAsync(CancellationToken ct)
    {
        try
        {
            var moduleInfo = await _ui.RunAsync(() =>
            {
                return _vbe.GetActiveModule();
            }).ConfigureAwait(false);

            var selectionInfo = await _ui.RunAsync(() =>
            {
                return _vbe.GetSelection();
            }).ConfigureAwait(false);

            var selectionText = selectionInfo.Text ?? "";
            var truncated = false;
            if (selectionText.Length > 32 * 1024)
            {
                selectionText = selectionText.Substring(0, 32 * 1024) + "\n... [truncado]";
                truncated = true;
            }

            return new VbeContext(
                moduleInfo.Name,
                moduleInfo.Type != default ? moduleInfo.Type.ToString() : null,
                selectionText,
                truncated);
        }
        catch (Exception ex)
        {
            _logger.Warn("VbeContextProvider", "Error obteniendo contexto VBE", ex);
            return new VbeContext(null, null, "", false);
        }
    }
}
