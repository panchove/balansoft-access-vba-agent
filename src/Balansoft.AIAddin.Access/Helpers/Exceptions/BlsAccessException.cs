// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Error funcional de Access (objeto no encontrado, permiso, etc.).
/// JSON-RPC -32000 con categoría "AccessException".
/// </summary>
public sealed class BlsAccessException : BlsException
{
    public BlsAccessException(string message) : base(message) { }
}
