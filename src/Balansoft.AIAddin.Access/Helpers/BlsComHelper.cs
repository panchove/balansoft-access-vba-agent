// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Reflection;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.Access.Helpers
{
    public static class BlsComHelper
    {
        public static dynamic GetVbe(dynamic accessApp)
        {
            // Access.Application has a VBE property returning the VBE object
            return accessApp.VBE;
        }

        public static dynamic GetCodeModule(dynamic vbe, string moduleName)
        {
            foreach (dynamic vbComp in vbe.VBProjects[0].VBComponents)
            {
                if (vbComp.Name == moduleName)
                {
                    return vbComp.CodeModule;
                }
            }
            return null;
        }

        public static void AddReference(dynamic vbe, string guid, int major, int minor)
        {
            var refs = vbe.ActiveVBProject.References;
            try
            {
                refs.AddFromGuid(guid, major, minor);
            }
            catch { }
        }

        public static object? AddToolsMenuItem(object? vbe, string caption, string tag)
        {
            if (vbe == null) return null;
            try
            {
                dynamic v = vbe;
                dynamic commandBars = v.CommandBars;
                if (commandBars == null) return null;

                object? toolsMenu = null;

                // 1. Busqueda directa por nombre (Tools / Herramientas / Mocks de tests)
                try
                {
                    toolsMenu = commandBars["Tools"];
                }
                catch { }

                if (toolsMenu == null)
                {
                    try
                    {
                        toolsMenu = commandBars["Herramientas"];
                    }
                    catch { }
                }

                // 2. Busqueda universal por control incorporado ID 30007
                if (toolsMenu == null)
                {
                    try
                    {
                        toolsMenu = commandBars.FindControl(Type: 10, Id: 30007);
                    }
                    catch { }
                }

                // 3. Fallback: inspeccionar la barra de menus principal (CommandBars[1])
                if (toolsMenu == null)
                {
                    try
                    {
                        dynamic menuBar = commandBars[1];
                        if (menuBar != null && menuBar.Controls != null)
                        {
                            int count = (int)menuBar.Controls.Count;
                            for (int i = 1; i <= count; i++)
                            {
                                dynamic ctrl = menuBar.Controls[i];
                                string cap = (string)ctrl.Caption ?? "";
                                if ((int)ctrl.Id == 30007 ||
                                    cap.IndexOf("Tools", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    cap.IndexOf("Herramientas", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    toolsMenu = ctrl;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (toolsMenu == null) return null;

                dynamic menu = toolsMenu;
                dynamic controls = menu.Controls;

                // Anadir boton de comando
                object button = controls.Add(Type: 1, Temporary: true);
                button.GetType().InvokeMember("Caption", BindingFlags.SetProperty, null, button, new object[] { caption });
                button.GetType().InvokeMember("Tag", BindingFlags.SetProperty, null, button, new object[] { tag });
                return button;
            }
            catch
            {
                return null;
            }
        }

        public static IntPtr GetVbeHwnd(object? vbe)
        {
            if (vbe == null) return IntPtr.Zero;
            try
            {
                dynamic v = vbe;
                return (IntPtr)(long)v.MainWindow.HWnd;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static ProjectKeyInfo GetProjectKey(object? accessApp, object? vbe)
        {
            try
            {
                if (vbe != null)
                {
                    try
                    {
                        dynamic v = vbe;
                        dynamic project = v.ActiveVBProject;
                        if (project != null)
                        {
                            string desc = (string)project.Description ?? "";
                            const string prefix = "BALANSOFT-PROJECT-KEY:";
                            if (desc.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                var val = desc.Substring(prefix.Length).Trim();
                                if (System.Text.RegularExpressions.Regex.IsMatch(val, @"^[A-Za-z0-9_-]{1,64}$"))
                                {
                                    return new ProjectKeyInfo { Key = val, Ephemeral = false, Source = "override" };
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (accessApp != null)
                {
                    try
                    {
                        dynamic app = accessApp;
                        dynamic currentProj = app.CurrentProject;
                        string fullName = (string)currentProj?.FullName ?? "";
                        if (!string.IsNullOrWhiteSpace(fullName))
                        {
                            var normalized = fullName.ToLowerInvariant().Replace('/', '\\');
                            using var sha = System.Security.Cryptography.SHA256.Create();
                            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
                            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                            var key = hex.Length >= 32 ? hex.Substring(0, 32) : hex;
                            return new ProjectKeyInfo { Key = key, Ephemeral = false, Source = "path" };
                        }
                    }
                    catch { }
                }

                string projName = "UnknownProject";
                if (vbe != null)
                {
                    try
                    {
                        dynamic v = vbe;
                        projName = (string)v.ActiveVBProject?.Name ?? "UnknownProject";
                    }
                    catch { }
                }
                var seed = $"{projName}|{Environment.MachineName}|{Environment.UserName}";
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
                    var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                    var key = hex.Length >= 32 ? hex.Substring(0, 32) : hex;
                    return new ProjectKeyInfo { Key = key, Ephemeral = true, Source = "ephemeral" };
                }
            }
            catch
            {
                return new ProjectKeyInfo { Key = "00000000000000000000000000000000", Ephemeral = true, Source = "ephemeral" };
            }
        }

        public static SelectionInfo GetSelection(object? vbe)
        {
            if (vbe == null) return SelectionInfo.Empty;
            try
            {
                dynamic v = vbe;
                dynamic activePane = v.ActiveCodePane;
                if (activePane == null) return SelectionInfo.Empty;
                int startLine = 0, startCol = 0, endLine = 0, endCol = 0;
                activePane.GetSelection(out startLine, out startCol, out endLine, out endCol);
                dynamic codeMod = activePane.CodeModule;
                string modName = codeMod?.Name ?? "";
                string? text = null;
                if (codeMod != null && startLine > 0 && endLine >= startLine)
                {
                    int count = endLine - startLine + 1;
                    text = (string)codeMod.Lines(startLine, count);
                }
                return new SelectionInfo
                {
                    ModuleName = modName,
                    StartLine = startLine,
                    StartColumn = startCol,
                    EndLine = endLine,
                    EndColumn = endCol,
                    Text = text ?? "",
                    HasSelection = !string.IsNullOrEmpty(text)
                };
            }
            catch
            {
                return SelectionInfo.Empty;
            }
        }

        public static ModuleInfo GetActiveModule(object? vbe)
        {
            if (vbe == null) return ModuleInfo.Empty;
            try
            {
                dynamic v = vbe;
                dynamic activePane = v.ActiveCodePane;
                if (activePane == null) return ModuleInfo.Empty;
                dynamic codeMod = activePane.CodeModule;
                if (codeMod == null) return ModuleInfo.Empty;
                string name = codeMod.Name ?? "";
                dynamic vbComp = codeMod.Parent;
                int compType = vbComp != null ? (int)vbComp.Type : 1;
                string projName = (string)v.ActiveVBProject?.Name ?? "";
                return new ModuleInfo
                {
                    Name = name,
                    Type = (VbeComponentType)compType,
                    ProjectName = projName
                };
            }
            catch
            {
                return ModuleInfo.Empty;
            }
        }

        public static System.Collections.Generic.IReadOnlyList<string> ListModules(object? vbe)
        {
            if (vbe == null) return Array.Empty<string>();
            var list = new System.Collections.Generic.List<string>();
            try
            {
                dynamic v = vbe;
                dynamic components = v.ActiveVBProject?.VBComponents;
                if (components != null)
                {
                    int count = (int)components.Count;
                    for (int i = 1; i <= count; i++)
                    {
                        dynamic comp = components.Item(i);
                        string name = (string)comp.Name;
                        if (!string.IsNullOrEmpty(name))
                        {
                            list.Add(name);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static string ReadModule(object? vbe, string name)
        {
            if (vbe == null) return string.Empty;
            try
            {
                dynamic v = vbe;
                dynamic comp = v.ActiveVBProject?.VBComponents?.Item(name);
                if (comp != null)
                {
                    dynamic cm = comp.CodeModule;
                    int count = (int)cm.CountOfLines;
                    if (count > 0)
                    {
                        return (string)cm.Lines(1, count);
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public static void WriteModule(object? vbe, string name, string code)
        {
            if (vbe == null) return;
            try
            {
                dynamic v = vbe;
                dynamic comp = v.ActiveVBProject?.VBComponents?.Item(name);
                if (comp != null)
                {
                    dynamic cm = comp.CodeModule;
                    int count = (int)cm.CountOfLines;
                    if (count > 0)
                    {
                        cm.DeleteLines(1, count);
                    }
                    if (!string.IsNullOrEmpty(code))
                    {
                        cm.AddFromString(code);
                    }
                }
            }
            catch { }
        }

        public static void CreateModule(object? vbe, string name, string code, VbeComponentType type)
        {
            if (vbe == null) return;
            try
            {
                dynamic v = vbe;
                dynamic components = v.ActiveVBProject?.VBComponents;
                if (components != null)
                {
                    dynamic comp = components.Add((int)type);
                    if (!string.IsNullOrEmpty(name))
                    {
                        comp.Name = name;
                    }
                    if (!string.IsNullOrEmpty(code))
                    {
                        comp.CodeModule.AddFromString(code);
                    }
                }
            }
            catch { }
        }

        public static void DeleteModule(object? vbe, string name, bool confirm)
        {
            if (vbe == null || !confirm) return;
            try
            {
                dynamic v = vbe;
                dynamic comp = v.ActiveVBProject?.VBComponents?.Item(name);
                if (comp != null)
                {
                    v.ActiveVBProject.VBComponents.Remove(comp);
                }
            }
            catch { }
        }

        public static ProjectSummaryResult GetProjectSummary(object? accessApp, IBlsVbe vbe)
        {
            string name = "Unknown";
            string version = "14.0";
            var counts = new ObjectCounts(0, 0, 0, 0, 0);
            var keyInfo = vbe.GetProjectKey();
            if (accessApp != null)
            {
                try
                {
                    dynamic app = accessApp;
                    name = (string)app.CurrentProject?.Name ?? "Unknown";
                    version = (string)app.Version ?? "14.0";
                }
                catch { }
            }
            return new ProjectSummaryResult(name, keyInfo.Key, keyInfo.Ephemeral, counts, vbe.ListModules().Count, version, "x86");
        }

        public static ListObjectsResult ListAccessObjects(object? accessApp)
        {
            return new ListObjectsResult(
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>());
        }

        public static ObjectDetailResult GetObjectDetail(object? accessApp, string name, string type, bool includeSql, int maxFields)
        {
            return new ObjectDetailResult(
                Name: name,
                Type: type,
                FieldCount: 0,
                Fields: Array.Empty<Balansoft.AIAddin.AccessIntegration.FieldInfo>(),
                LastModified: DateTime.UtcNow.ToString("o"),
                TruncatedFields: false);
        }
    }
}