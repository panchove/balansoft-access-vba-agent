// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;
using System.Text;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Constructor del system prompt para NIM.
/// </summary>
internal sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    private const string BasePrompt = "Eres un asistente integrado en el editor VBA de Microsoft Access.\n\n" +
        "Reglas fundamentales:\n" +
        "- Si una tool falla, propaga el error al usuario con la informacion recibida. NUNCA inventes que la operacion tuvo exito.\n" +
        "- Si no estas seguro de si una operacion se completó, verifica con otra tool antes de afirmar nada.\n" +
        "- Trabaja en el proyecto Access actual.\n" +
        "- Puedes leer y escribir modulos VBA usando las herramientas disponibles.\n" +
        "- Responde siempre en espanol.\n\n" +
        "Herramientas disponibles:\n" +
        "- list_modules: lista todos los modulos del proyecto VBA\n" +
        "- read_module: devuelve el codigo fuente de un modulo\n" +
        "- write_module: sobrescribe el codigo de un modulo existente\n" +
        "- create_module: crea un modulo nuevo (Standard, Class, Form)\n" +
        "- delete_module: elimina un modulo (requiere confirm:true)\n" +
        "- get_selection: devuelve modulo + rango + texto seleccionado\n" +
        "- get_active_module: devuelve informacion del modulo activo\n" +
        "- project_summary: resumen del proyecto Access (nombre, projectKey, conteos)\n" +
        "- list_objects: lista objetos por categoria (tablas, queries, formularios, reportes, macros)\n" +
        "- object_detail: detalle de un objeto especifico (campos, SQL, controles)\n" +
        "- create_form: crea un formulario Access con controles (TextBox, Label, CommandButton) y lo guarda\n" +
        "- create_table: crea una tabla en Access especificando nombre, columnas, tipos y clave primaria\n" +
        "- create_query: crea o actualiza una consulta SQL o vista guardada (QueryDef)\n" +
        "- execute_sql: ejecuta cualquier sentencia SQL DDL/DML (CREATE TABLE, INSERT, UPDATE, ALTER, etc.)\n" +
        "- create_ribbon: crea o actualiza una cinta de opciones (Ribbon XML) en la tabla del sistema USysRibbons de Access\n" +
        "- list_commandbars: lista las barras de herramientas y menus clasicos (CommandBars) de Access 2003\n" +
        "- migrate_commandbar_to_ribbon: convierte automaticamente un menu clasico CommandBar en un Ribbon XML moderno en USysRibbons\n\n" +
        "Proyecto actual:\n" +
        "- Nombre: {ProjectName}\n" +
        "- ProjectKey: {ProjectKey}\n" +
        "- Tablas: {TableCount}, Queries: {QueryCount}, Formularios: {FormCount}, Reportes: {ReportCount}, Macros: {MacroCount}\n" +
        "- Modulos VBA: {ModuleCount}";

    private const string ActiveModuleTemplate = "\nContexto VBE actual:\n" +
        "- Modulo activo: {ActiveModuleName} ({ActiveModuleType})\n" +
        "- Seleccion actual:\n{SelectionText}";

    private const string NoModuleTemplate = "\nContexto VBE actual:\n" +
        "- No hay modulo activo\n" +
        "- Sin seleccion";

    private const string SelectionTruncatedNote = "\n[Seleccion truncada a 32 KB]";

    public string Build(VbeContext context, IReadOnlyList<string> availableToolNames)
    {
        var sb = new StringBuilder();

        sb.AppendLine(BasePrompt);

        sb.AppendLine("\nHerramientas disponibles en este turno:");
        foreach (var tool in availableToolNames)
        {
            sb.AppendLine("- " + tool);
        }

        if (!string.IsNullOrEmpty(context.ActiveModuleName))
        {
            var selectionText = context.SelectionText ?? "(sin seleccion)";
            if (context.SelectionTruncated)
            {
                sb.Append(SelectionTruncatedNote);
            }

            sb.AppendLine(ActiveModuleTemplate
                .Replace("{ActiveModuleName}", context.ActiveModuleName)
                .Replace("{ActiveModuleType}", context.ActiveModuleType ?? "Standard")
                .Replace("{SelectionText}", selectionText));
        }
        else
        {
            sb.AppendLine(NoModuleTemplate);
        }

        sb.AppendLine("\n[Proyecto actual sera inyectado aqui]");

        return sb.ToString();
    }
}
