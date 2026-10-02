// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// ExcepciÃ³n COM envuelta con categorÃ­a mapeada.
/// JSON-RPC -32000 con categorÃ­a "ComException" y HRESULT en data.
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
/// CategorÃ­a de error COM.
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
