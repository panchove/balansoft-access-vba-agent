// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI.Nim;

namespace Balansoft.AIAddin.UI;

/// <summary>
/// Receptor de eventos del cliente NIM. La terminal implementa esta interfaz.
/// </summary>
public interface IBlsNimSink
{
    /// <summary>
    /// Chunk de texto recibido del stream.
    /// </summary>
    void OnTextChunk(string text);

    /// <summary>
    /// Tool call completo ensamblado desde fragmentos.
    /// </summary>
    void OnToolCall(ToolCallComplete call);

    /// <summary>
    /// Error fatal durante el streaming.
    /// </summary>
    void OnError(BlsNimException ex);

    /// <summary>
    /// Stream completado exitosamente (recibido [DONE]).
    /// </summary>
    void OnDone();

    /// <summary>
    /// Usuario cancelÃ³ la operaciÃ³n (no es error).
    /// </summary>
    void OnCancelled();
}
