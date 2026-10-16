// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// Implementación fake de IBlsAccess para tests unitarios.
/// </summary>
public sealed class FakeAccess : IBlsAccess
{
    public ProjectSummaryResult ProjectSummary()
    {
        return new ProjectSummaryResult(
            Name: "TestBase.accdb",
            ProjectKey: "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
            Ephemeral: false,
            Counts: new ObjectCounts(Tables: 2, Queries: 1, Forms: 1, Reports: 0, Macros: 1),
            ModuleCount: 3,
            AccessVersion: "16.0",
            Bitness: "x86");
    }

    public ListObjectsResult ListObjects()
    {
        return new ListObjectsResult(
            Tables: new[]
            {
                new ObjectListItem("Clientes", 5),
                new ObjectListItem("Pedidos", 4)
            },
            Queries: new[]
            {
                new ObjectListItem("qClientesActivos", 5)
            },
            Forms: new[]
            {
                new ObjectListItem("frmClientes")
            },
            Reports: new ObjectListItem[0],
            Macros: new[]
            {
                new ObjectListItem("mcrInicio")
            });
    }

    public ObjectDetailResult ObjectDetail(string name, string type, bool includeSql, int maxFields)
    {
        // Filtrar objetos de sistema
        if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("~", StringComparison.Ordinal))
        {
            throw new BlsInvalidParamsException($"Objeto de sistema no accesible: {name}");
        }

        return type switch
        {
            "Table" => GetTableDetail(name, includeSql, maxFields),
            "Query" => GetQueryDetail(name, includeSql, maxFields),
            "Form" => GetFormDetail(name, maxFields),
            "Report" => GetReportDetail(name, maxFields),
            "Macro" => GetMacroDetail(name),
            _ => throw new BlsInvalidParamsException($"type inválido: {type}. Válidos: Table,Query,Form,Report,Macro")
        };
    }

    private ObjectDetailResult GetTableDetail(string name, bool includeSql, int maxFields)
    {
        var fields = new List<FieldInfo>
        {
            new("Id", "Long", 4, true, "Identificador"),
            new("Nombre", "Text", 50, true, "Nombre del cliente"),
            new("Email", "Text", 120, false, "Correo electrónico"),
            new("FechaAlta", "DateTime", 8, false, "Fecha de alta"),
            new("Activo", "YesNo", 1, true, "Cliente activo")
        };

        return new ObjectDetailResult(
            Name: name,
            Type: "Table",
            FieldCount: fields.Count,
            Fields: fields.Count > maxFields ? fields.GetRange(0, maxFields) : fields,
            LastModified: "2025-01-01T00:00:00Z",
            TruncatedFields: fields.Count > maxFields,
            RecordCount: 100,
            Sql: includeSql ? "SELECT * FROM Clientes" : null,
            SqlTruncated: false);
    }

    private ObjectDetailResult GetQueryDetail(string name, bool includeSql, int maxFields)
    {
        var fields = new List<FieldInfo>
        {
            new("Id", "Long", 4, true, ""),
            new("Nombre", "Text", 50, true, "")
        };

        return new ObjectDetailResult(
            Name: name,
            Type: "Query",
            FieldCount: fields.Count,
            Fields: fields,
            LastModified: "2025-01-01T00:00:00Z",
            TruncatedFields: false,
            RecordCount: 50,
            Sql: includeSql ? "SELECT Id, Nombre FROM Clientes WHERE Activo = True" : null,
            SqlTruncated: false);
    }

    private ObjectDetailResult GetFormDetail(string name, int maxFields)
    {
        var controls = new List<ControlInfo>
        {
            new("txtNombre", "TextBox"),
            new("btnGuardar", "CommandButton"),
            new("lblTitulo", "Label")
        };

        return new ObjectDetailResult(
            Name: name,
            Type: "Form",
            FieldCount: 0,
            Fields: Array.Empty<FieldInfo>(),
            LastModified: "2025-01-01T00:00:00Z",
            TruncatedFields: false,
            Controls: controls,
            ControlsTruncated: false);
    }

    private ObjectDetailResult GetReportDetail(string name, int maxFields)
    {
        return new ObjectDetailResult(
            Name: name,
            Type: "Report",
            FieldCount: 0,
            Fields: Array.Empty<FieldInfo>(),
            LastModified: "2025-01-01T00:00:00Z",
            TruncatedFields: false);
    }

    private ObjectDetailResult GetMacroDetail(string name)
    {
        return new ObjectDetailResult(
            Name: name,
            Type: "Macro",
            FieldCount: 0,
            Fields: Array.Empty<FieldInfo>(),
            LastModified: "2025-01-01T00:00:00Z",
            TruncatedFields: false,
            MacroType: "Standalone");
    }

    public CreateFormResult CreateForm(string name, string? caption, IReadOnlyList<CreateFormControl>? controls)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
        int count = controls?.Count ?? 0;
        return new CreateFormResult(name, true, count, $"Formulario fake '{name}' creado con {count} controles.");
    }

    public TableOpResult CreateTable(string name, IReadOnlyList<CreateColumnSpec> columns)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
        BlsContract.RequiresNotNull(columns, nameof(columns));
        return new TableOpResult(name, true, columns.Count, $"Tabla fake '{name}' creada con {columns.Count} columnas.");
    }

    public QueryOpResult CreateQuery(string name, string sql)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
        BlsContract.RequiresNotNullOrWhiteSpace(sql, nameof(sql));
        return new QueryOpResult(name, true, $"Consulta fake '{name}' creada con éxito.");
    }

    public SqlExecutionResult ExecuteSql(string sql)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(sql, nameof(sql));
        return new SqlExecutionResult(true, 1, "SQL fake ejecutado con éxito.");
    }

    public RibbonOpResult CreateRibbon(string name, string ribbonXml)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
        BlsContract.RequiresNotNullOrWhiteSpace(ribbonXml, nameof(ribbonXml));
        return new RibbonOpResult(name, true, $"Ribbon fake '{name}' creado con éxito.");
    }
}
