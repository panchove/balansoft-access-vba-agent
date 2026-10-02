// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// ParÃ¡metros invÃ¡lidos en una operaciÃ³n (JSON-RPC -32602).
/// </summary>
public sealed class BlsInvalidParamsException : BlsException
{
    public BlsInvalidParamsException(string message) : base(message) { }
}
