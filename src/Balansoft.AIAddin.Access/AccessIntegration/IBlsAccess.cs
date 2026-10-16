// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;

namespace Balansoft.AIAddin.AccessIntegration;

/// <summary>
/// Resultado de project_summary.
/// </summary>
public sealed record ProjectSummaryResult(
    string Name,
    string ProjectKey,
    bool Ephemeral,
    ObjectCounts Counts,
    int ModuleCount,
    string AccessVersion,
    string Bitness);

/// <summary>
/// Conteos por categoría de objeto.
/// </summary>
public sealed record ObjectCounts(
    int Tables,
    int Queries,
    int Forms,
    int Reports,
    int Macros);

/// <summary>
/// Elemento en list_objects.
/// </summary>
public sealed record ObjectListItem(
    string Name,
    int? FieldCount = null);

/// <summary>
/// Resultado de list_objects.
/// </summary>
public sealed record ListObjectsResult(
    IReadOnlyList<ObjectListItem> Tables,
    IReadOnlyList<ObjectListItem> Queries,
    IReadOnlyList<ObjectListItem> Forms,
    IReadOnlyList<ObjectListItem> Reports,
    IReadOnlyList<ObjectListItem> Macros);

/// <summary>
/// Campo en object_detail.
/// </summary>
public sealed record FieldInfo(
    string Name,
    string Type,
    int Size,
    bool Required,
    string Description);

/// <summary>
/// Control en formulario/reporte.
/// </summary>
public sealed record ControlInfo(
    string Name,
    string Type);

/// <summary>
/// Resultado base de object_detail.
/// </summary>
public sealed record ObjectDetailResult(
    string Name,
    string Type,
    int FieldCount,
    IReadOnlyList<FieldInfo> Fields,
    string LastModified,
    bool TruncatedFields,
    int? RecordCount = null,
    string? Sql = null,
    bool SqlTruncated = false,
    IReadOnlyList<ControlInfo>? Controls = null,
    bool ControlsTruncated = false,
    string? MacroType = null);

/// <summary>
/// Especificación de un control a crear en un formulario Access.
/// </summary>
public sealed record CreateFormControl(
    string Type,
    string Name,
    string? Label = null,
    string? Caption = null,
    bool IsPassword = false);

/// <summary>
/// Resultado de crear un formulario Access.
/// </summary>
public sealed record CreateFormResult(
    string Name,
    bool Success,
    int ControlCount,
    string? Message = null);

/// <summary>
/// Especificación de columna para creación de tablas en Access.
/// </summary>
public sealed record CreateColumnSpec(
    string Name,
    string Type,
    int? Size = null,
    bool PrimaryKey = false,
    bool Required = false);

/// <summary>
/// Resultado de crear una tabla Access.
/// </summary>
public sealed record TableOpResult(
    string Name,
    bool Success,
    int ColumnCount,
    string? Message = null);

/// <summary>
/// Resultado de crear una consulta/vista Access.
/// </summary>
public sealed record QueryOpResult(
    string Name,
    bool Success,
    string? Message = null);

/// <summary>
/// Resultado de ejecutar una instrucción DDL/DML SQL en Access.
/// </summary>
public sealed record SqlExecutionResult(
    bool Success,
    int RecordsAffected,
    string? Message = null);

/// <summary>
/// Resultado de registrar un Ribbon XML en USysRibbons.
/// </summary>
public sealed record RibbonOpResult(
    string Name,
    bool Success,
    string? Message = null);

/// <summary>
/// Elemento o botón en una CommandBar clásica de Access.
/// </summary>
public sealed record CommandBarControlInfo(
    string Caption,
    string? OnAction = null,
    int? FaceId = null,
    string? Tooltip = null);

/// <summary>
/// Información de una CommandBar personalizada clásica de Access 2003.
/// </summary>
public sealed record CommandBarInfo(
    string Name,
    int Type,
    bool BuiltIn,
    IReadOnlyList<CommandBarControlInfo> Controls);

/// <summary>
/// Resultado de migrar una CommandBar clásica a Ribbon XML.
/// </summary>
public sealed record MigrateCommandBarResult(
    string CommandBarName,
    string RibbonName,
    bool Success,
    string RibbonXml,
    string? Message = null);

/// <summary>
/// Contrato de operaciones sobre objetos Access.
/// Implementado por BlsAccessService.
/// </summary>
public interface IBlsAccess
{
    /// <summary>Resumen del proyecto Access.</summary>
    ProjectSummaryResult ProjectSummary();

    /// <summary>Lista objetos por categoría (sin SQL, sin campos, sin controles).</summary>
    ListObjectsResult ListObjects();

    /// <summary>Detalle de un objeto específico.</summary>
    ObjectDetailResult ObjectDetail(string name, string type, bool includeSql, int maxFields);

    /// <summary>Crea un nuevo formulario Access con sus controles y lo guarda.</summary>
    CreateFormResult CreateForm(string name, string? caption, IReadOnlyList<CreateFormControl>? controls);

    /// <summary>Crea una nueva tabla en Access con las columnas especificadas.</summary>
    TableOpResult CreateTable(string name, IReadOnlyList<CreateColumnSpec> columns);

    /// <summary>Crea o reemplaza una consulta o vista SQL (QueryDef) en Access.</summary>
    QueryOpResult CreateQuery(string name, string sql);

    /// <summary>Ejecuta una sentencia SQL DDL/DML (CREATE TABLE, INSERT, UPDATE, ALTER, etc.) en CurrentDb.</summary>
    SqlExecutionResult ExecuteSql(string sql);

    /// <summary>Crea o actualiza una cinta de opciones (Ribbon XML) en la tabla USysRibbons de Access.</summary>
    RibbonOpResult CreateRibbon(string name, string ribbonXml);

    /// <summary>Lista las barras de herramientas y menús CommandBars personalizados del proyecto.</summary>
    IReadOnlyList<CommandBarInfo> ListCommandBars(bool customOnly = true);

    /// <summary>Migra una CommandBar clásica de Access 2003 a un Ribbon XML moderno en USysRibbons.</summary>
    MigrateCommandBarResult MigrateCommandBarToRibbon(string commandBarName, string? targetRibbonName = null);
}
