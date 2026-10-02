// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Error funcional del VBE (mÃ³dulo no encontrado, ya existe, etc.).
/// JSON-RPC -32000 con categorÃ­a "VbeException".
/// </summary>
public sealed class BlsVbeException : BlsException
{
    public BlsVbeException(string message) : base(message) { }
}
