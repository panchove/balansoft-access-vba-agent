// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
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

        public CreateFormResult CreateForm(string name, string? caption, IReadOnlyList<CreateFormControl>? controls)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            return BlsComHelper.CreateAccessForm(_accessApp, name, caption, controls);
        }

        public TableOpResult CreateTable(string name, IReadOnlyList<CreateColumnSpec> columns)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            BlsContract.RequiresNotNull(columns, nameof(columns));
            BlsContract.Requires(columns.Count > 0, "Debe especificar al menos una columna para la tabla.");
            return BlsComHelper.CreateAccessTable(_accessApp, name, columns);
        }

        public QueryOpResult CreateQuery(string name, string sql)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            BlsContract.RequiresNotNullOrWhiteSpace(sql, nameof(sql));
            return BlsComHelper.CreateAccessQuery(_accessApp, name, sql);
        }

        public SqlExecutionResult ExecuteSql(string sql)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(sql, nameof(sql));
            return BlsComHelper.ExecuteAccessSql(_accessApp, sql);
        }

        public RibbonOpResult CreateRibbon(string name, string ribbonXml)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            BlsContract.RequiresNotNullOrWhiteSpace(ribbonXml, nameof(ribbonXml));
            return BlsComHelper.CreateAccessRibbon(_accessApp, name, ribbonXml);
        }
    }
}
