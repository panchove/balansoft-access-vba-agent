// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Parámetros inválidos en una operación (JSON-RPC -32602).
/// </summary>
public sealed class BlsInvalidParamsException : BlsException
{
    public BlsInvalidParamsException(string message) : base(message) { }
}
