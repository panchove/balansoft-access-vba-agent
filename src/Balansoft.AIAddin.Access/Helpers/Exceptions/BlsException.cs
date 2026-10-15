// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Excepción base del proyecto. Todas las excepciones tipadas heredan de esta.
/// </summary>
public abstract class BlsException : Exception
{
    protected BlsException(string message) : base(message) { }
    protected BlsException(string message, Exception inner) : base(message, inner) { }
}
