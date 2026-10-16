// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Nombres de métodos JSON-RPC soportados por el bridge.
/// </summary>
public static class JsonRpcMethods
{
    // VBE (7)
    public const string ListModules      = "list_modules";
    public const string ReadModule       = "read_module";
    public const string WriteModule      = "write_module";
    public const string CreateModule     = "create_module";
    public const string DeleteModule     = "delete_module";
    public const string GetSelection     = "get_selection";
    public const string GetActiveModule  = "get_active_module";

    // Access read-only (3)
    public const string ProjectSummary   = "project_summary";
    public const string ListObjects      = "list_objects";
    public const string ObjectDetail     = "object_detail";

    // Access authoring (4)
    public const string CreateForm       = "create_form";
    public const string CreateTable      = "create_table";
    public const string CreateQuery      = "create_query";
    public const string ExecuteSql       = "execute_sql";

    /// <summary>
    /// Conjunto inmutable con los métodos para validación de whitelist.
    /// </summary>
    public static readonly IReadOnlyCollection<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ListModules, ReadModule, WriteModule, CreateModule, DeleteModule,
            GetSelection, GetActiveModule, ProjectSummary, ListObjects, ObjectDetail,
            CreateForm, CreateTable, CreateQuery, ExecuteSql
        };
}
