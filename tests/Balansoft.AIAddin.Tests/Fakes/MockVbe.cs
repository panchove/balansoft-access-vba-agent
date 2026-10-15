// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Balansoft.AIAddin.VbeIntegration;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// Implementación fake de IBlsVbe para tests unitarios.
/// </summary>
public sealed class MockVbe : IBlsVbe
{
    public bool IsAvailable { get; set; } = true;
    public List<string> Modules { get; } = new List<string>();
    public Dictionary<string, string> Code { get; } = new Dictionary<string, string>();
    public SelectionInfo Selection { get; set; } = SelectionInfo.Empty;
    public ModuleInfo ActiveModule { get; set; } = ModuleInfo.Empty;

    public ProjectKeyInfo ProjectKey { get; set; } = new ProjectKeyInfo
    {
        Key = "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
        Ephemeral = false,
        Source = "path"
    };

    /// <summary>
    /// Excepción a lanzar en la próxima invocación de una operación VBE.
    /// Permite verificar el mapeo de errores del dispatcher.
    /// </summary>
    public Exception NextException { get; set; }

    /// <summary>
    /// Nombres de los métodos que realmente se ejecutaron, en orden.
    /// Permite verificar que una cancelación no llegó a ejecutar la herramienta.
    /// </summary>
    public List<string> ExecutedMethods { get; } = new List<string>();

    public void Reset()
    {
        NextException = null;
        ExecutedMethods.Clear();
    }

    public IReadOnlyList<string> ListModules()
    {
        ExecutedMethods.Add("list_modules");
        var injected = TakeInjection();
        if (injected != null) throw injected;
        return Modules;
    }

    public string ReadModule(string name)
    {
        ExecutedMethods.Add("read_module");
        var injected = TakeInjection();
        if (injected != null) throw injected;

        if (!Code.TryGetValue(name, out var code))
            throw new BlsVbeException($"Módulo '{name}' no encontrado");
        return code;
    }

    public void WriteModule(string name, string codeToWrite)
    {
        ExecutedMethods.Add("write_module");
        var injected = TakeInjection();
        if (injected != null) throw injected;

        if (!Code.ContainsKey(name))
            throw new BlsVbeException($"Módulo '{name}' no encontrado");
        Code[name] = codeToWrite;
    }

    public void CreateModule(string name, string code, VbeComponentType type)
    {
        ExecutedMethods.Add("create_module");
        var injected = TakeInjection();
        if (injected != null) throw injected;

        if (Modules.Any(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase)))
            throw new BlsVbeException($"Módulo '{name}' ya existe");

        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,30}$"))
            throw new BlsInvalidParamsException($"Nombre de módulo inválido: {name}");

        Modules.Add(name);
        Code[name] = code;
    }

    public void DeleteModule(string name, bool confirm)
    {
        ExecutedMethods.Add("delete_module");
        var injected = TakeInjection();
        if (injected != null) throw injected;

        if (!confirm)
            throw new BlsInvalidParamsException("delete_module requiere confirm:true");

        if (!Modules.Contains(name))
            throw new BlsVbeException($"Módulo '{name}' no encontrado");

        if (Modules.Count == 1)
            throw new BlsVbeException("No se puede borrar el último módulo del proyecto");

        Modules.Remove(name);
        Code.Remove(name);
    }

    public SelectionInfo GetSelection()
    {
        ExecutedMethods.Add("get_selection");
        var injected = TakeInjection();
        if (injected != null) throw injected;
        return Selection;
    }

    public ModuleInfo GetActiveModule()
    {
        ExecutedMethods.Add("get_active_module");
        var injected = TakeInjection();
        if (injected != null) throw injected;
        return ActiveModule;
    }

    public ProjectKeyInfo GetProjectKey()
    {
        ExecutedMethods.Add("get_project_key");
        var injected = TakeInjection();
        if (injected != null) throw injected;
        return ProjectKey;
    }

    /// <summary>
    /// Devuelve y consume la excepción inyectada, o null si no hay ninguna.
    /// </summary>
    private Exception TakeInjection()
    {
        var injected = NextException;
        NextException = null;
        return injected;
    }
}

