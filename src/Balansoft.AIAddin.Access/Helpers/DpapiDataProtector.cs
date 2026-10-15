// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Security.Cryptography;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Implementación real de IDataProtector usando DPAPI (CurrentUser).
/// </summary>
internal sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Balansoft.AIAddin.Access.v1");

    public byte[] Protect(byte[] plaintext)
        => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext)
        => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
