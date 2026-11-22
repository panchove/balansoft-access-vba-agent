// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Security.Cryptography;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Protector v1 con entropía fija. Se conserva únicamente como fallback de
/// lectura durante la migración a entropía derivada (ADR-018 / R-11); las
/// escrituras nuevas usan <see cref="DerivedEntropyDpapiDataProtector"/>.
/// </summary>
internal sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Balansoft.AIAddin.Access.v1");

    public byte[] Protect(byte[] plaintext)
        => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext)
        => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
