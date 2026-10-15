// Copyright © 2026 Balansoft
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
/// Información de selección en el editor de código.
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
/// Información del módulo activo.
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
/// Información de la clave de proyecto (ADR-001).
/// </summary>
public readonly struct ProjectKeyInfo
{
    public string Key { get; init; }
    public bool Ephemeral { get; init; }
    public string Source { get; init; } // "override" | "path" | "ephemeral"
}

/// <summary>
/// Contrato de automatización VBE. Implementado por BlsVbeService.
/// </summary>
public interface IBlsVbe
{
    /// <summary>Indica si el VBE está disponible (no null, no runtime).</summary>
    bool IsAvailable { get; }

    /// <summary>Lista los nombres de todos los módulos del proyecto VBA activo.</summary>
    IReadOnlyList<string> ListModules();

    /// <summary>Devuelve el código fuente completo de un módulo.</summary>
    string ReadModule(string name);

    /// <summary>Sobrescribe el código de un módulo existente.</summary>
    void WriteModule(string name, string code);

    /// <summary>Crea un módulo nuevo.</summary>
    void CreateModule(string name, string code, VbeComponentType type);

    /// <summary>Elimina un módulo (requiere confirm:true).</summary>
    void DeleteModule(string name, bool confirm);

    /// <summary>Devuelve la selección actual en el editor de código.</summary>
    SelectionInfo GetSelection();

    /// <summary>Devuelve información del módulo activo.</summary>
    ModuleInfo GetActiveModule();

    /// <summary>Devuelve la identidad del proyecto (ADR-001).</summary>
    ProjectKeyInfo GetProjectKey();
}
