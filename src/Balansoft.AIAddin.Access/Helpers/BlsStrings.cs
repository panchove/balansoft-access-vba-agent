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
                    { "LangLabel", "Idioma:" },
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
                    { "LangLabel", "Language:" },
                }
            },
            {
                "pt", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AppTitle", "NIM Terminal" },
                    { "MenuItem", "NIM Terminal" },
                    { "BtnNewSession", "+ Nova" },
                    { "BtnDeleteSession", "Excluir" },
                    { "MsgTurnInProgress", "Uma consulta está em andamento. Pare antes de {0}." },
                    { "MsgSelectSession", "Selecione uma sessão para {0}." },
                    { "MsgConfirmDelete", "Excluir a sessão \"{0}\"?\nEsta ação não pode ser desfeita." },
                    { "MsgConfirmDeleteTitle", "{0} - Excluir sessão" },
                    { "MsgSessionCreated", "Nova sessão criada" },
                    { "MsgSessionLoaded", "Sessão carregada: {0}" },
                    { "MsgSessionDeleted", "Sessão excluída: {0}" },
                    { "MsgHistoryNotPersistent", "Histórico não persistente" },
                    { "MsgReady", "Pronto" },
                    { "MsgErrorInitializing", "Erro ao inicializar o terminal" },
                    { "MsgErrorChangingSession", "Erro ao alterar a sessão: {0}" },
                    { "MsgErrorCreatingSession", "Erro ao criar nova sessão: {0}" },
                    { "MsgErrorDeletingSession", "Erro ao excluir sessão: {0}" },
                    { "MsgNoServices", "Não foi possível inicializar todos os serviços do terminal NIM." },
                    { "MsgNoVbeProject", "Não é possível abrir o terminal NIM sem um projeto VBA aberto." },
                    { "StatusError", "Erro" },
                    { "ColumnRole", "Função" },
                    { "ColumnMessage", "Mensagem" },
                    { "DetailTitle", "DETALHE DA MENSAGEM / CÓDIGO SELECIONADO" },
                    { "BtnCopyDetail", "Copiar Detalhe" },
                    { "BtnClearDetail", "Limpar Detalhe" },
                    { "BtnStop", "Parar" },
                    { "BtnSend", "Enviar" },
                    { "StatusReady", "Pronto" },
                    { "StatusViewingDetail", "Pronto, visualizando detalhe..." },
                    { "StatusDetailCopied", "Detalhe copiado para a área de transferência" },
                    { "SessionInitialTitle", "Sessão inicial" },
                    { "MsgContextError", "Erro ao obter contexto: {0}" },
                    { "RoleAssistant", "Assistente" },
                    { "MsgContextHeader", "[Contexto VBE Atual]\nMódulo: {0} ({1})\nSeleção: {2} caracteres{3}" },
                    { "MsgNoActiveModule", "(sem módulo ativo)" },
                    { "MsgUnknownModule", "(desconhecido)" },
                    { "StatusThinking", "Pensando..." },
                    { "MsgErrorPrefix", "Erro: {0}" },
                    { "TooltipExplain", "Explicar código ativo" },
                    { "TooltipDoc", "Documentar funções" },
                    { "TooltipRefactor", "Refatorar e otimizar" },
                    { "TooltipTest", "Gerar testes VBA" },
                    { "TooltipContext", "Ver contexto VBE" },
                    { "TooltipClear", "Limpar / Nova sessão" },
                    { "PromptExplain", "Por favor, explique em detalhe o código do módulo ativo ou da seleção atual, sua estrutura e possíveis áreas de melhoria." },
                    { "PromptDoc", "Por favor, revise o módulo atual e gere comentários de documentação padrão (descrição, parâmetros, valor de retorno) para cada função e procedimento." },
                    { "PromptRefactor", "Por favor, analise o código atual e proponha uma refatoração limpa e otimizada, mantendo a lógica funcional mas melhorando legibilidade e boas práticas de VBA." },
                    { "PromptTest", "Gere procedimentos de teste de unidade em VBA para verificar as funções do módulo ativo com asserções para casos típicos e limites." },
                }
            },
            {
                "fr", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AppTitle", "NIM Terminal" },
                    { "MenuItem", "NIM Terminal" },
                    { "BtnNewSession", "+ Nouvelle" },
                    { "BtnDeleteSession", "Supprimer" },
                    { "MsgTurnInProgress", "Une requête est en cours. Arrêtez-la avant {0}." },
                    { "MsgSelectSession", "Sélectionnez une session pour {0}." },
                    { "MsgConfirmDelete", "Supprimer la session \"{0}\"?\nCette action est irréversible." },
                    { "MsgConfirmDeleteTitle", "{0} - Supprimer la session" },
                    { "MsgSessionCreated", "Nouvelle session créée" },
                    { "MsgSessionLoaded", "Session chargée: {0}" },
                    { "MsgSessionDeleted", "Session supprimée: {0}" },
                    { "MsgHistoryNotPersistent", "Historique non persistant" },
                    { "MsgReady", "Prêt" },
                    { "MsgErrorInitializing", "Erreur lors de l'initialisation du terminal" },
                    { "MsgErrorChangingSession", "Erreur lors du changement de session: {0}" },
                    { "MsgErrorCreatingSession", "Erreur lors de la création de la nouvelle session: {0}" },
                    { "MsgErrorDeletingSession", "Erreur lors de la suppression de la session: {0}" },
                    { "MsgNoServices", "Impossible d'initialiser tous les services du terminal NIM." },
                    { "MsgNoVbeProject", "Impossible d'ouvrir le terminal NIM sans un projet VBA ouvert." },
                    { "StatusError", "Erreur" },
                    { "ColumnRole", "Rôle" },
                    { "ColumnMessage", "Message" },
                    { "DetailTitle", "DÉTAIL DU MESSAGE / CODE SÉLECTIONNÉ" },
                    { "BtnCopyDetail", "Copier le Détail" },
                    { "BtnClearDetail", "Effacer le Détail" },
                    { "BtnStop", "Arrêter" },
                    { "BtnSend", "Envoyer" },
                    { "StatusReady", "Prêt" },
                    { "StatusViewingDetail", "Prêt, affichage du détail..." },
                    { "StatusDetailCopied", "Détail copié dans le presse-papiers" },
                    { "SessionInitialTitle", "Session initiale" },
                    { "MsgContextError", "Erreur lors de l'obtention du contexte: {0}" },
                    { "RoleAssistant", "Assistant" },
                    { "MsgContextHeader", "[Contexte VBE Actuel]\nModule: {0} ({1})\nSélection: {2} caractères{3}" },
                    { "MsgNoActiveModule", "(aucun module actif)" },
                    { "MsgUnknownModule", "(inconnu)" },
                    { "StatusThinking", "Réflexion..." },
                    { "MsgErrorPrefix", "Erreur: {0}" },
                    { "TooltipExplain", "Expliquer le code actif" },
                    { "TooltipDoc", "Documenter les fonctions" },
                    { "TooltipRefactor", "Refactoriser et optimiser" },
                    { "TooltipTest", "Générer des tests VBA" },
                    { "TooltipContext", "Voir le contexte VBE" },
                    { "TooltipClear", "Effacer / Nouvelle session" },
                    { "PromptExplain", "Veuillez expliquer en détail le code du module actif ou de la sélection actuelle, sa structure et les domaines possibles d'amélioration." },
                    { "PromptDoc", "Veuillez examiner le module actuel et générer des commentaires de documentation standard (description, paramètres, valeur de retour) pour chaque fonction et procédure." },
                    { "PromptRefactor", "Veuillez analyser le code actuel et proposer un refactoring propre et optimisé, en conservant la logique fonctionnelle mais en améliorant la lisibilité et les bonnes pratiques VBA." },
                    { "PromptTest", "Générer des procédures de test unitaire en VBA pour vérifier les fonctions du module actif avec des assertions pour les cas typiques et les limites." },
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

        /// <summary>
        /// Obtiene la lista de códigos de idioma soportados.
        /// </summary>
        public static IEnumerable<string> GetSupportedLanguages()
        {
            return _texts.Keys;
        }
    }
}
