// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// ExcepciÃ³n lanzada cuando un frame supera el tamaÃ±o mÃ¡ximo permitido (1 MB).
/// </summary>
public sealed class BlsFrameTooLargeException : BlsException
{
    public BlsFrameTooLargeException(uint length)
        : base($"Frame demasiado grande: {length} bytes (mÃ¡x 1 MB)") { }
}

/// <summary>
/// ExcepciÃ³n lanzada cuando se produce timeout a mitad de un frame.
/// </summary>
public sealed class BlsFrameIncompleteException : BlsException
{
    public BlsFrameIncompleteException(string message)
        : base(message) { }
}
