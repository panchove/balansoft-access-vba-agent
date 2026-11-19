// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Security.Cryptography;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Deriva la entropía v2 de DPAPI a partir de datos no secretos del equipo
/// (R-11). El resultado es estable entre procesos del mismo usuario y máquina,
/// pero no está embebido como literal en el ensamblado.
/// </summary>
internal static class BlsMachineEntropy
{
    private const string Version = "Balansoft.AIAddin.Access.v2";

    public static byte[] Current()
    {
        var seed = string.Concat(
            Version, "|",
            Environment.MachineName, "|",
            Environment.UserName);

        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
    }
}
