// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;

namespace Balansoft.AIAddin.VbeIntegration;

/// <summary>
/// Tipo de componente VBE.
/// </summary>
public enum VbeComponentType
{
    Standard = 1, // vbext_ct_StdModule
    Class    = 2, // vbext_ct_ClassModule
    Form     = 3  // vbext_ct_MSForm
}

/// <summary>
/// InformaciÃ³n de selecciÃ³n en el editor de cÃ³digo.
/// </summary>
public readonly struct SelectionInfo
{
    public static readonly SelectionInfo Empty = new()
    {
        ModuleName = null,
        StartLine = 0,
        StartColumn = 0,
        EndLine = 0,
        EndColumn = 0,
        Text = "",
        HasSelection = false
    };

    public string? ModuleName { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string Text { get; init; }
    public bool HasSelection { get; init; }
}

/// <summary>
/// InformaciÃ³n del mÃ³dulo activo.
/// </summary>
public readonly struct ModuleInfo
{
    public static readonly ModuleInfo Empty = new()
    {
        Name = null,
        Type = VbeComponentType.Standard,
        ProjectName = null
    };

    public string? Name { get; init; }
    public VbeComponentType Type { get; init; }
    public string? ProjectName { get; init; }
}

/// <summary>
/// InformaciÃ³n de la clave de proyecto (ADR-001).
/// </summary>
public readonly struct ProjectKeyInfo
{
    public string Key { get; init; }
    public bool Ephemeral { get; init; }
    public string Source { get; init; } // "override" | "path" | "ephemeral"
}

/// <summary>
/// Contrato de automatizaciÃ³n VBE. Implementado por BlsVbeService.
/// </summary>
public interface IBlsVbe
{
    /// <summary>Indica si el VBE estÃ¡ disponible (no null, no runtime).</summary>
    bool IsAvailable { get; }

    /// <summary>Lista los nombres de todos los mÃ³dulos del proyecto VBA activo.</summary>
    IReadOnlyList<string> ListModules();

    /// <summary>Devuelve el cÃ³digo fuente completo de un mÃ³dulo.</summary>
    string ReadModule(string name);

    /// <summary>Sobrescribe el cÃ³digo de un mÃ³dulo existente.</summary>
    void WriteModule(string name, string code);

    /// <summary>Crea un mÃ³dulo nuevo.</summary>
    void CreateModule(string name, string code, VbeComponentType type);

    /// <summary>Elimina un mÃ³dulo (requiere confirm:true).</summary>
    void DeleteModule(string name, bool confirm);

    /// <summary>Devuelve la selecciÃ³n actual en el editor de cÃ³digo.</summary>
    SelectionInfo GetSelection();

    /// <summary>Devuelve informaciÃ³n del mÃ³dulo activo.</summary>
    ModuleInfo GetActiveModule();

    /// <summary>Devuelve la identidad del proyecto (ADR-001).</summary>
    ProjectKeyInfo GetProjectKey();
}
