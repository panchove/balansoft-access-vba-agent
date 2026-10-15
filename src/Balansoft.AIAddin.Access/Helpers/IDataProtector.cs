// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Abstracción del cifrado DPAPI. Inyectable para tests.
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
