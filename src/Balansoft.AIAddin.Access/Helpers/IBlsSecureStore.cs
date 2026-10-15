// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Almacén seguro para la API key (DPAPI).
/// </summary>
public interface IBlsSecureStore
{
    /// <summary>
    /// Intenta obtener la API key descifrada.
    /// </summary>
    /// <param name="key">Key descifrada si existe.</param>
    /// <returns>True si existe y se descifró correctamente; false si no existe.</returns>
    /// <exception cref="BlsSecureStoreException">Si el archivo existe pero no se puede descifrar.</exception>
    bool TryGetApiKey(out string? key);

    /// <summary>
    /// Guarda la API key cifrada con DPAPI.
    /// </summary>
    /// <param name="key">API key a guardar.</param>
    /// <exception cref="BlsInvalidParamsException">Si key es null, vacío o solo whitespace.</exception>
    void SaveApiKey(string key);

    /// <summary>
    /// Elimina la API key guardada.
    /// </summary>
    void DeleteApiKey();
}
