// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Constructor del system prompt que incluye contexto VBE y reglas de comportamiento.
/// </summary>
public interface ISystemPromptBuilder
{
    /// <summary>
    /// Construye el system prompt completo.
    /// </summary>
    /// <param name="context">Contexto VBE actual (mÃ³dulo activo, selecciÃ³n).</param>
    /// <param name="availableToolNames">Nombres de las tools disponibles.</param>
    /// <returns>System prompt completo.</param>
    string Build(VbeContext context, IReadOnlyList<string> availableToolNames);
}
