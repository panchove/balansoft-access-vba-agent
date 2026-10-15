// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using Balansoft.AIAddin.Access.Helpers;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.AccessIntegration
{
    /// <summary>
    /// Implementación de operaciones sobre objetos Access usando enlace tardío vía BlsComHelper.
    /// </summary>
    public sealed class BlsAccessService : IBlsAccess
    {
        private readonly object? _accessApp;
        private readonly IBlsVbe _vbe;
        private readonly IBlsLogger _logger;

        public BlsAccessService(object? accessApp, IBlsVbe vbe, IBlsLogger logger)
        {
            _accessApp = accessApp;
            _vbe = vbe;
            _logger = logger;
        }

        public ProjectSummaryResult ProjectSummary() => BlsComHelper.GetProjectSummary(_accessApp, _vbe);

        public ListObjectsResult ListObjects() => BlsComHelper.ListAccessObjects(_accessApp);

        public ObjectDetailResult ObjectDetail(string name, string type, bool includeSql, int maxFields)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            BlsContract.RequiresNotNullOrWhiteSpace(type, nameof(type));
            BlsContract.Requires(maxFields > 0, "maxFields debe ser un entero positivo.");
            return BlsComHelper.GetObjectDetail(_accessApp, name, type, includeSql, maxFields);
        }
    }
}
