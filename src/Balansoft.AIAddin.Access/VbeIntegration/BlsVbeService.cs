// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.VbeIntegration
{
    /// <summary>
    /// Implementación de automatización VBE usando enlace tardío vía BlsComHelper.
    /// </summary>
    public sealed class BlsVbeService : IBlsVbe
    {
        private readonly object? _vbe;
        private readonly object? _accessApp;
        private readonly IBlsLogger _logger;

        public BlsVbeService(object? vbe, object? accessApp, IBlsLogger logger)
        {
            _vbe = vbe;
            _accessApp = accessApp;
            _logger = logger;
        }

        public bool IsAvailable => _vbe != null;

        public IReadOnlyList<string> ListModules() => BlsComHelper.ListModules(_vbe);

        public string ReadModule(string name) => BlsComHelper.ReadModule(_vbe, name);

        public void WriteModule(string name, string code) => BlsComHelper.WriteModule(_vbe, name, code);

        public void CreateModule(string name, string code, VbeComponentType type) => BlsComHelper.CreateModule(_vbe, name, code, type);

        public void DeleteModule(string name, bool confirm) => BlsComHelper.DeleteModule(_vbe, name, confirm);

        public SelectionInfo GetSelection() => BlsComHelper.GetSelection(_vbe);

        public ModuleInfo GetActiveModule() => BlsComHelper.GetActiveModule(_vbe);

        public ProjectKeyInfo GetProjectKey() => BlsComHelper.GetProjectKey(_accessApp, _vbe);
    }
}
