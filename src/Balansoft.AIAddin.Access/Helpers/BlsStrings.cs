// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 26514:2022
// Architecture Reference: P12 (Español en UI y logs)

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Proveedor de textos de interfaz y mensajes de log con soporte de idiomas.
    /// Idioma por defecto: español (es). No requiere dependencias externas.
    /// Para agregar un idioma, añadir una entrada al diccionario <see cref="_texts"/>.
    /// </summary>
    public static class BlsStrings
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "es", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AppTitle", "NIM Terminal" },
                    { "MenuItem", "NIM Terminal" },
                    { "BtnNewSession", "+ Nueva" },
                    { "BtnDeleteSession", "Eliminar" },
                    { "MsgTurnInProgress", "Hay una consulta en ejecución. Deténla antes de {0}." },
                    { "MsgSelectSession", "Selecciona una sesión para {0}." },
                    { "MsgConfirmDelete", "¿Eliminar la sesión \"{0}\"?\nEsta acción no se puede deshacer." },
                    { "MsgConfirmDeleteTitle", "{0} - Eliminar sesión" },
                    { "MsgSessionCreated", "Nueva sesión creada" },
                    { "MsgSessionLoaded", "Sesión cargada: {0}" },
                    { "MsgSessionDeleted", "Sesión eliminada: {0}" },
                    { "MsgHistoryNotPersistent", "Historial no persistente" },
                    { "MsgReady", "Listo" },
                    { "MsgErrorInitializing", "Error al inicializar la terminal" },
                    { "MsgErrorChangingSession", "Error al cambiar de sesión: {0}" },
                    { "MsgErrorCreatingSession", "Error creando nueva sesión: {0}" },
                    { "MsgErrorDeletingSession", "Error eliminando sesión: {0}" },
                    { "MsgNoServices", "No se pudieron inicializar todos los servicios de la terminal NIM." },
                    { "MsgNoVbeProject", "No se puede abrir la terminal NIM sin un proyecto de VBA abierto." },
                    { "StatusError", "Error" },
                    { "ColumnRole", "Rol" },
                    { "ColumnMessage", "Mensaje" },
                    { "DetailTitle", "DETALLE DEL MENSAJE / CÓDIGO SELECCIONADO" },
                    { "BtnCopyDetail", "Copiar Detalle" },
                    { "BtnClearDetail", "Limpiar Detalle" },
                    { "BtnStop", "Detener" },
                    { "BtnSend", "Enviar" },
                    { "StatusReady", "Listo" },
                    { "StatusViewingDetail", "Listo, visualizando detalle..." },
                    { "StatusDetailCopied", "Detalle copiado al portapapeles" },
                    { "SessionInitialTitle", "Sesión inicial" },
                    { "MsgContextError", "Error obteniendo contexto: {0}" },
                    { "RoleAssistant", "Asistente" },
                    { "MsgContextHeader", "[Contexto VBE Actual]\nMódulo: {0} ({1})\nSelección: {2} caracteres{3}" },
                    { "MsgNoActiveModule", "(sin módulo activo)" },
                    { "MsgUnknownModule", "(desconocido)" },
                    { "StatusThinking", "Pensando..." },
                    { "MsgErrorPrefix", "Error: {0}" },
                    { "TooltipExplain", "Explicar código activo" },
                    { "TooltipDoc", "Documentar funciones" },
                    { "TooltipRefactor", "Refactorizar y optimizar" },
                    { "TooltipTest", "Generar tests VBA" },
                    { "TooltipContext", "Ver contexto VBE" },
                    { "TooltipClear", "Limpiar / Nueva sesión" },
                    { "PromptExplain", "Por favor, explica en detalle el código del módulo activo o la selección actual, su estructura y posibles áreas de mejora." },
                    { "PromptDoc", "Por favor, revisa el módulo actual y genera comentarios de documentación estándar (descripción, parámetros, valor retornado) para cada función y procedimiento." },
                    { "PromptRefactor", "Por favor, analiza el código actual y propón una refactorización limpia y optimizada, manteniendo la lógica funcional pero mejorando legibilidad y buenas prácticas VBA." },
                    { "PromptTest", "Genera procedimientos de prueba unitaria en VBA para verificar las funciones del módulo activo con aserciones de casos típicos y límites." },
                    { "MsgSoloEspanol", "Este texto solo existe en español" },
                }
            },
            {
                "en", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AppTitle", "NIM Terminal" },
                    { "MenuItem", "NIM Terminal" },
                    { "BtnNewSession", "+ New" },
                    { "BtnDeleteSession", "Delete" },
                    { "MsgTurnInProgress", "A query is in progress. Stop it before {0}." },
                    { "MsgSelectSession", "Select a session to {0}." },
                    { "MsgConfirmDelete", "Delete session \"{0}\"?\nThis action cannot be undone." },
                    { "MsgConfirmDeleteTitle", "{0} - Delete session" },
                    { "MsgSessionCreated", "New session created" },
                    { "MsgSessionLoaded", "Session loaded: {0}" },
                    { "MsgSessionDeleted", "Session deleted: {0}" },
                    { "MsgHistoryNotPersistent", "History not persistent" },
                    { "MsgReady", "Ready" },
                    { "MsgErrorInitializing", "Error initializing the terminal" },
                    { "MsgErrorChangingSession", "Error changing session: {0}" },
                    { "MsgErrorCreatingSession", "Error creating new session: {0}" },
                    { "MsgErrorDeletingSession", "Error deleting session: {0}" },
                    { "MsgNoServices", "Could not initialize all NIM terminal services." },
                    { "MsgNoVbeProject", "Cannot open NIM Terminal without an open VBA project." },
                    { "StatusError", "Error" },
                    { "ColumnRole", "Role" },
                    { "ColumnMessage", "Message" },
                    { "DetailTitle", "MESSAGE / CODE SELECTED DETAIL" },
                    { "BtnCopyDetail", "Copy Detail" },
                    { "BtnClearDetail", "Clear Detail" },
                    { "BtnStop", "Stop" },
                    { "BtnSend", "Send" },
                    { "StatusReady", "Ready" },
                    { "StatusViewingDetail", "Ready, viewing detail..." },
                    { "StatusDetailCopied", "Detail copied to clipboard" },
                    { "SessionInitialTitle", "Initial session" },
                    { "MsgContextError", "Error getting context: {0}" },
                    { "RoleAssistant", "Assistant" },
                    { "MsgContextHeader", "[Current VBE Context]\nModule: {0} ({1})\nSelection: {2} characters{3}" },
                    { "MsgNoActiveModule", "(no active module)" },
                    { "MsgUnknownModule", "(unknown)" },
                    { "StatusThinking", "Thinking..." },
                    { "MsgErrorPrefix", "Error: {0}" },
                    { "TooltipExplain", "Explain active code" },
                    { "TooltipDoc", "Document functions" },
                    { "TooltipRefactor", "Refactor and optimize" },
                    { "TooltipTest", "Generate VBA tests" },
                    { "TooltipContext", "View VBE context" },
                    { "TooltipClear", "Clear / New session" },
                    { "PromptExplain", "Please explain in detail the code of the active module or current selection, its structure and possible areas for improvement." },
                    { "PromptDoc", "Please review the current module and generate standard documentation comments (description, parameters, return value) for each function and procedure." },
                    { "PromptRefactor", "Please analyze the current code and propose a clean and optimized refactoring, maintaining functional logic but improving readability and best practices for VBA." },
                    { "PromptTest", "Generate VBA unit test procedures to verify the functions of the active module with assertions for typical cases and limits." },
                }
            },
        };

        private static string _currentLanguage = "es";

        /// <summary>
        /// Idioma actual (código ISO 639-1, ej. "es", "en").
        /// </summary>
        public static string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("El código de idioma no puede estar vacío.", nameof(value));
                var code = value.Trim().ToLowerInvariant();
                if (!_texts.ContainsKey(code))
                    throw new ArgumentException($"Idioma no soportado: {code}. Idiomas disponibles: {string.Join(", ", _texts.Keys)}.", nameof(value));
                _currentLanguage = code;
            }
        }

        /// <summary>
        /// Obtiene el texto para la clave dada en el idioma actual, aplicando <see cref="string.Format"/> con los argumentos opcionales.
        /// </summary>
        /// <param name="key">Clave del texto.</param>
        /// <param name="args">Argumentos para formato.</param>
        /// <returns>Texto localizado, o la propia clave si no existe.</returns>
        public static string Get(string key, params object?[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
                return key;

            if (!_texts.TryGetValue(_currentLanguage, out var dict) ||
                !dict.TryGetValue(key, out var text))
            {
                // Fallback a español si el idioma actual no tiene la clave
                if (_currentLanguage != "es" && _texts.TryGetValue("es", out var esDict) &&
                    esDict.TryGetValue(key, out var esText))
                {
                    text = esText;
                }
                else
                {
                    return key;
                }
            }

            if (args != null && args.Length > 0)
            {
                try
                {
                    return string.Format(CultureInfo.InvariantCulture, text, args);
                }
                catch (FormatException)
                {
                    return text;
                }
            }

            return text;
        }

        /// <summary>
        /// Indica si el idioma dado está soportado.
        /// </summary>
        public static bool IsLanguageSupported(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return false;
            return _texts.ContainsKey(language.Trim().ToLowerInvariant());
        }
    }
}
