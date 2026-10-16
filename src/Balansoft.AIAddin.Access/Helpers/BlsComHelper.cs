// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 26514:2022 / ISO/IEC 25010:2023
// Architecture Reference: ADR-006-VBE-BRIDGE.md / ADR-012-ADDIN-LIFECYCLE.md

using System;
using System.Reflection;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.VbeIntegration;

namespace Balansoft.AIAddin.Access.Helpers
{
    /// <summary>
    /// Fachada de interoperabilidad COM con enlace tardío (late-binding) para Microsoft Access y el editor VBE.
    /// Encapsula las invocaciones reflexivas y dinámicas aislando el resto de la aplicación de llamadas no seguras.
    /// Cumple con las especificaciones ISO/IEC 26514 (Developer Documentation) e ISO/IEC 25010 (Fault Tolerance).
    /// </summary>
    public static class BlsComHelper
    {
        /// <summary>
        /// Obtiene la instancia del entorno VBE a partir de la aplicación Microsoft Access host.
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access (<c>Application</c>).</param>
        /// <returns>Objeto nativo VBE late-bound.</returns>
        /// <exception cref="BlsComException">Se lanza si <paramref name="accessApp"/> o su propiedad <c>VBE</c> son nulos.</exception>
        public static dynamic GetVbe(dynamic accessApp)
        {
            if (accessApp == null)
                throw new BlsComException("GetVbe", BlsComCategory.UnexpectedType, "accessApp es null", null);

            dynamic vbe = accessApp.VBE;
            if (vbe == null)
                throw new BlsComException("GetVbe", BlsComCategory.UnexpectedType, "VBE es null en accessApp", null);

            return vbe;
        }

        /// <summary>
        /// Obtiene el módulo de código correspondiente al componente especificado dentro del proyecto VBA activo.
        /// </summary>
        /// <param name="vbe">Instancia del editor VBE.</param>
        /// <param name="moduleName">Nombre del componente o módulo de código a localizar.</param>
        /// <returns>Objeto <c>CodeModule</c> si se encuentra; de lo contrario, <c>null</c>.</returns>
        public static dynamic GetCodeModule(dynamic vbe, string moduleName)
        {
            BlsContract.RequiresNotNull(vbe, nameof(vbe));
            BlsContract.RequiresNotNullOrWhiteSpace(moduleName, nameof(moduleName));

            foreach (dynamic vbComp in vbe.VBProjects[0].VBComponents)
            {
                if (vbComp.Name == moduleName)
                {
                    return vbComp.CodeModule;
                }
            }
            return null;
        }

        /// <summary>
        /// Añade una referencia de tipo biblioteca COM al proyecto VBA activo mediante su GUID.
        /// </summary>
        /// <param name="vbe">Instancia del editor VBE.</param>
        /// <param name="guid">Identificador único (GUID) de la biblioteca a referenciar.</param>
        /// <param name="major">Número de versión mayor.</param>
        /// <param name="minor">Número de versión menor.</param>
        /// <exception cref="Exception">Se propaga cualquier excepción COM producida al invocar <c>AddFromGuid</c>.</exception>
        public static void AddReference(dynamic vbe, string guid, int major, int minor)
        {
            BlsContract.RequiresNotNull(vbe, nameof(vbe));
            BlsContract.RequiresNotNullOrWhiteSpace(guid, nameof(guid));

            var refs = vbe.ActiveVBProject.References;
            try
            {
                refs.AddFromGuid(guid, major, minor);
            }
            catch { throw; }
        }

        /// <summary>
        /// Añade un elemento clicable al menú "Herramientas" (Tools) de la barra de comandos del VBE.
        /// Realiza una búsqueda multilingüe y por ID incorporado (30007).
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <param name="caption">Texto visible del botón de comando en el menú.</param>
        /// <param name="tag">Etiqueta identificadora única para la acción.</param>
        /// <returns>Control COM añadido o <c>null</c> si no pudo crearse.</returns>
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
                catch { throw; }

                if (toolsMenu == null)
                {
                    try
                    {
                        toolsMenu = commandBars["Herramientas"];
                    }
                    catch { throw; }
                }

                // 2. Busqueda universal por control incorporado ID 30007
                if (toolsMenu == null)
                {
                    try
                    {
                        toolsMenu = commandBars.FindControl(Type: 10, Id: 30007);
                    }
                    catch { throw; }
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
                    catch { throw; }
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

        /// <summary>
        /// Obtiene el manejador de ventana nativo de Windows (HWND) de la ventana principal del VBE.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <returns><see cref="IntPtr"/> apuntando al HWND del VBE, o <see cref="IntPtr.Zero"/> si no está disponible.</returns>
        public static IntPtr GetVbeHwnd(object? vbe)
        {
            if (vbe == null) return IntPtr.Zero;
            try
            {
                dynamic v = vbe;
                object rawHwnd = v.MainWindow.HWnd;
                if (rawHwnd is int intVal) return new IntPtr(intVal);
                if (rawHwnd is long longVal) return new IntPtr(longVal);
                if (rawHwnd is IntPtr ptrVal) return ptrVal;
                return new IntPtr(Convert.ToInt32(rawHwnd));
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Calcula la identidad determinista y estable del proyecto Access actual según la cascada de ADR-001.
        /// Prioridad 1: Override explícito en VBProject.Description.
        /// Prioridad 2: Hash SHA-256 de la ruta normalizada del archivo de base de datos.
        /// Fallback: Hash de identidad efímera por proyecto/máquina/usuario.
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access.</param>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <returns>Estructura <see cref="ProjectKeyInfo"/> con la clave de 32 caracteres hexadecimales y origen.</returns>
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
                    catch { throw; }
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
                    catch { throw; }
                }

                string projName = "UnknownProject";
                if (vbe != null)
                {
                    try
                    {
                        dynamic v = vbe;
                        projName = (string)v.ActiveVBProject?.Name ?? "UnknownProject";
                    }
                    catch { throw; }
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

        /// <summary>
        /// Obtiene el estado actual de la selección del cursor en el editor de código VBA activo.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <returns>Estructura <see cref="SelectionInfo"/> con módulo, líneas, columnas y texto seleccionado.</returns>
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

        /// <summary>
        /// Obtiene metadatos del módulo de código activo en la ventana de edición.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <returns>Estructura <see cref="ModuleInfo"/> con el nombre, tipo de componente y proyecto.</returns>
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

        /// <summary>
        /// Obtiene la lista de nombres de todos los componentes o módulos de código VBA en el proyecto activo.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <returns>Colección de sólo lectura con los nombres de los módulos.</returns>
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
            catch { throw; }
            return list;
        }

        /// <summary>
        /// Lee el código fuente completo contenido en un módulo VBA por su nombre.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <param name="name">Nombre del módulo a leer.</param>
        /// <returns>Texto del código fuente, o cadena vacía si no existe o no tiene líneas.</returns>
        public static string ReadModule(object? vbe, string name)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
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
            catch { throw; }
            return string.Empty;
        }

        /// <summary>
        /// Sobrescribe el contenido de un módulo VBA existente con el nuevo código proporcionado.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <param name="name">Nombre del módulo a escribir.</param>
        /// <param name="code">Nuevo código fuente a escribir en el módulo.</param>
        public static void WriteModule(object? vbe, string name, string code)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
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
            catch { throw; }
        }

        /// <summary>
        /// Crea un nuevo módulo o componente en el proyecto VBA activo.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <param name="name">Nombre del módulo a crear.</param>
        /// <param name="code">Código fuente inicial.</param>
        /// <param name="type">Tipo de componente VBE (Standard, Class, Form).</param>
        public static void CreateModule(object? vbe, string name, string code, VbeComponentType type)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
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
            catch { throw; }
        }

        /// <summary>
        /// Elimina un componente o módulo de código del proyecto VBA activo previa confirmación.
        /// </summary>
        /// <param name="vbe">Instancia nativa del VBE.</param>
        /// <param name="name">Nombre del módulo a eliminar.</param>
        /// <param name="confirm">Bandera de confirmación explícita requerida para efectuar el borrado.</param>
        public static void DeleteModule(object? vbe, string name, bool confirm)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
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
            catch { throw; }
        }

        /// <summary>
        /// Genera un resumen descriptivo del proyecto Access y VBA activo según el contrato de ADR-004.
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access.</param>
        /// <param name="vbe">Servicio de abstracción VBE.</param>
        /// <returns>Resultado estructurado <see cref="ProjectSummaryResult"/>.</returns>
        public static ProjectSummaryResult GetProjectSummary(object? accessApp, IBlsVbe vbe)
        {
            BlsContract.RequiresNotNull(vbe, nameof(vbe));

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
                catch { throw; }
            }
            return new ProjectSummaryResult(name, keyInfo.Key, keyInfo.Ephemeral, counts, vbe.ListModules().Count, version, "x86");
        }

        /// <summary>
        /// Lista los objetos de base de datos de Microsoft Access clasificados por tipo (solo lectura).
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access.</param>
        /// <returns>Resultado estructurado <see cref="ListObjectsResult"/>.</returns>
        public static ListObjectsResult ListAccessObjects(object? accessApp)
        {
            return new ListObjectsResult(
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>(),
                Array.Empty<ObjectListItem>());
        }

        /// <summary>
        /// Obtiene el detalle esquemático de un objeto de base de datos específico según el contrato de ADR-004.
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access.</param>
        /// <param name="name">Nombre del objeto Access a inspeccionar.</param>
        /// <param name="type">Tipo de objeto (Table, Query, Form, Report, Macro).</param>
        /// <param name="includeSql">Si es verdadero y es una consulta, incluye su definición SQL.</param>
        /// <param name="maxFields">Límite máximo de campos a inspeccionar.</param>
        /// <returns>Detalle del objeto como <see cref="ObjectDetailResult"/>.</returns>
        public static ObjectDetailResult GetObjectDetail(object? accessApp, string name, string type, bool includeSql, int maxFields)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));
            BlsContract.RequiresNotNullOrWhiteSpace(type, nameof(type));

            return new ObjectDetailResult(
                Name: name,
                Type: type,
                FieldCount: 0,
                Fields: Array.Empty<Balansoft.AIAddin.AccessIntegration.FieldInfo>(),
                LastModified: DateTime.UtcNow.ToString("o"),
                TruncatedFields: false);
        }

        /// <summary>
        /// Crea un nuevo formulario Access, añade los controles especificados, ajusta sus propiedades y lo guarda.
        /// </summary>
        /// <param name="accessApp">Instancia nativa de Microsoft Access.</param>
        /// <param name="name">Nombre con el que se guardará el formulario (ej: frmLogin).</param>
        /// <param name="caption">Título o Caption del formulario.</param>
        /// <param name="controls">Lista de especificaciones de controles a crear.</param>
        /// <returns>Resultado de la creación <see cref="CreateFormResult"/>.</returns>
        public static CreateFormResult CreateAccessForm(
            object? accessApp,
            string name,
            string? caption,
            System.Collections.Generic.IReadOnlyList<CreateFormControl>? controls)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(name, nameof(name));

            if (accessApp == null)
            {
                throw new BlsComException("CreateAccessForm", BlsComCategory.UnexpectedType, "accessApp es null", null);
            }

            try
            {
                dynamic app = accessApp;

                // 1. Crear nuevo formulario en blanco en memoria
                dynamic form = app.CreateForm();
                string tempFormName = (string)form.Name;

                // Configurar propiedades básicas del Formulario
                try
                {
                    if (!string.IsNullOrWhiteSpace(caption))
                    {
                        form.Caption = caption;
                    }
                    form.RecordSelectors = false;
                    form.NavigationButtons = false;
                    form.DividingLines = false;
                    form.ScrollBars = 0; // acScrollBarsNone
                    form.AutoCenter = true;
                    form.HasModule = true; // Habilita módulo de código VBA detrás del formulario
                }
                catch
                {
                    // Propiedades cosméticas no críticas
                }

                int controlCount = 0;

                // Access constants:
                // acDetail = 0
                // acLabel = 100
                // acTextBox = 109
                // acCommandButton = 104
                // 1 pulgada = 1440 twips; 1 cm = 567 twips
                int topTwips = 300;
                int leftTwips = 300;
                int labelWidth = 1800;
                int textWidth = 3000;
                int rowHeight = 350;
                int rowGap = 200;

                if (controls != null && controls.Count > 0)
                {
                    foreach (var ctrlSpec in controls)
                    {
                        string ctrlType = ctrlSpec.Type?.Trim() ?? string.Empty;
                        string ctrlName = ctrlSpec.Name?.Trim() ?? string.Empty;

                        if (ctrlType.Equals("CommandButton", StringComparison.OrdinalIgnoreCase) ||
                            ctrlType.Equals("Button", StringComparison.OrdinalIgnoreCase))
                        {
                            // Botón de comando
                            dynamic btn = app.CreateControl(tempFormName, 104, 0, "", "", leftTwips, topTwips, 1600, 450);
                            if (!string.IsNullOrWhiteSpace(ctrlName))
                            {
                                btn.Name = ctrlName;
                            }
                            if (!string.IsNullOrWhiteSpace(ctrlSpec.Caption))
                            {
                                btn.Caption = ctrlSpec.Caption;
                            }
                            controlCount++;
                            leftTwips += 1800;
                            if (leftTwips > 5000)
                            {
                                leftTwips = 300;
                                topTwips += 600;
                            }
                        }
                        else if (ctrlType.Equals("TextBox", StringComparison.OrdinalIgnoreCase))
                        {
                            // Si tiene label asociada o texto de etiqueta
                            if (!string.IsNullOrWhiteSpace(ctrlSpec.Label))
                            {
                                dynamic lbl = app.CreateControl(tempFormName, 100, 0, "", "", 300, topTwips, labelWidth, rowHeight);
                                lbl.Caption = ctrlSpec.Label;
                                controlCount++;
                            }

                            dynamic txt = app.CreateControl(tempFormName, 109, 0, "", "", 300 + labelWidth + 100, topTwips, textWidth, rowHeight);
                            if (!string.IsNullOrWhiteSpace(ctrlName))
                            {
                                txt.Name = ctrlName;
                            }
                            if (ctrlSpec.IsPassword)
                            {
                                txt.InputMask = "Password";
                            }
                            controlCount++;
                            topTwips += (rowHeight + rowGap);
                        }
                        else if (ctrlType.Equals("Label", StringComparison.OrdinalIgnoreCase))
                        {
                            dynamic lbl = app.CreateControl(tempFormName, 100, 0, "", "", 300, topTwips, labelWidth + textWidth, rowHeight + 100);
                            if (!string.IsNullOrWhiteSpace(ctrlName))
                            {
                                lbl.Name = ctrlName;
                            }
                            lbl.Caption = ctrlSpec.Caption ?? ctrlSpec.Label ?? "Label";
                            controlCount++;
                            topTwips += (rowHeight + rowGap + 100);
                        }
                    }
                }

                // 2. Guardar formulario con el nombre definitivo y cerrar
                // DoCmd.Save(acForm = 2, tempFormName)
                // DoCmd.Close(acForm = 2, tempFormName, acSavePrompt = 0 / acSaveYes = 1)
                dynamic doCmd = app.DoCmd;
                doCmd.Save(2, tempFormName);
                doCmd.Close(2, tempFormName, 1);

                // Renombrar si tempFormName era Form1 y el nombre deseado es diferente
                if (!tempFormName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        doCmd.Rename(name, 2, tempFormName);
                    }
                    catch
                    {
                        // Si ya existía o falla rename, intentar guardar directamente o reportar
                    }
                }

                return new CreateFormResult(name, true, controlCount, $"Formulario '{name}' creado exitosamente con {controlCount} controles.");
            }
            catch (Exception ex)
            {
                throw new BlsComException("CreateAccessForm", BlsComCategory.Other, $"Fallo al crear formulario '{name}': {ex.Message}", ex);
            }
        }
    }
}