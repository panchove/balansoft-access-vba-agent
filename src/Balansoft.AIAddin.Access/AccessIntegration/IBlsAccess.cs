// Copyright Â© 2026 Balansoft
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
/// Conteos por categorÃ­a de objeto.
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
/// Contrato de solo lectura sobre objetos Access.
/// Implementado por BlsAccessObjectService.
/// </summary>
public interface IBlsAccess
{
    /// <summary>Resumen del proyecto Access.</summary>
    ProjectSummaryResult ProjectSummary();

    /// <summary>Lista objetos por categorÃ­a (sin SQL, sin campos, sin controles).</summary>
    ListObjectsResult ListObjects();

    /// <summary>Detalle de un objeto especÃ­fico.</summary>
    ObjectDetailResult ObjectDetail(string name, string type, bool includeSql, int maxFields);
}
