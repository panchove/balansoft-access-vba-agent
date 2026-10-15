// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Excepción COM envuelta con categoría mapeada.
/// JSON-RPC -32000 con categoría "ComException" y HRESULT en data.
/// </summary>
public sealed class BlsComException : BlsException
{
    public BlsComCategory Category { get; }
    public new int HResult { get; }
    public string Operation { get; }

    public BlsComException(string operation, BlsComCategory category, string message, COMException inner)
        : base(message, inner)
    {
        Operation = operation;
        Category = category;
        HResult = inner.HResult;
    }
}

/// <summary>
/// Categoría de error COM.
/// </summary>
public enum BlsComCategory
{
    NotRegistered,
    Busy,
    Rejected,
    Permission,
    UnexpectedType,
    Other
}
