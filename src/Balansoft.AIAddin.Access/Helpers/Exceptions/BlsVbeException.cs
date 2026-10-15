// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Error funcional del VBE (módulo no encontrado, ya existe, etc.).
/// JSON-RPC -32000 con categoría "VbeException".
/// </summary>
public sealed class BlsVbeException : BlsException
{
    public BlsVbeException(string message) : base(message) { }
}
