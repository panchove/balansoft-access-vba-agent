// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Security.Cryptography;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Protector DPAPI con entropía derivada del equipo (v2). Para escribir usa
/// siempre la entropía derivada; el protector de entropía fija se conserva
/// únicamente como fallback de lectura en <see cref="BlsSecureStore"/>.
/// </summary>
internal sealed class DerivedEntropyDpapiDataProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        BlsContract.RequiresNotNull(plaintext, nameof(plaintext));
        return ProtectedData.Protect(
            plaintext,
            BlsMachineEntropy.Current(),
            DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        BlsContract.RequiresNotNull(ciphertext, nameof(ciphertext));
        return ProtectedData.Unprotect(
            ciphertext,
            BlsMachineEntropy.Current(),
            DataProtectionScope.CurrentUser);
    }
}
