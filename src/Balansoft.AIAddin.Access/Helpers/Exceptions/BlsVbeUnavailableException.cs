// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// VBE no disponible (null, runtime, ACCDE, sin proyecto activo).
/// JSON-RPC -32000 con categoría "VbeUnavailableException".
/// </summary>
public sealed class BlsVbeUnavailableException : BlsException
{
    public BlsVbeUnavailableException(string message) : base(message) { }
}
