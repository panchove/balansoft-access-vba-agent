// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.UI.Nim;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Cliente para la API NIM con streaming SSE y tool calls.
/// </summary>
public interface IBlsNimClient : IDisposable
{
    /// <summary>
    /// Envía mensajes al modelo NIM y emite eventos al sink.
    /// </summary>
    /// <param name="messages">Historial + prompt actual.</param>
    /// <param name="tools">Definiciones de tools disponibles (puede ser vacío).</param>
    /// <param name="sink">Receptor de eventos (text chunks, tool calls, done/error/cancelled).</param>
    /// <param name="ct">Token de cancelación del turno (ADR-003).</param>
    Task SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IBlsNimSink sink,
        CancellationToken ct);
}
