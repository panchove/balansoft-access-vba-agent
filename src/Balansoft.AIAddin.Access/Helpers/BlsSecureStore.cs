// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 (Seguridad) / ISO/IEC 26514:2022
// Architecture Reference: ADR-009-SECURE-STORE.md

using System;
using System.IO;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Almacén seguro para la API key usando DPAPI (CurrentUser + entropía
/// derivada). Implementa las garantías de confidencialidad de ISO/IEC 25010,
/// ADR-009 y la migración transparente de ADR-018 (R-11).
/// </summary>
public sealed class BlsSecureStore : IBlsSecureStore
{
    private readonly IDataProtector _protector;
    private readonly IDataProtector? _legacyProtector;
    private readonly string _filePath;
    private readonly IBlsLogger _logger;

    public BlsSecureStore(
        IDataProtector protector,
        string filePath,
        IBlsLogger logger,
        IDataProtector? legacyProtector = null)
    {
        _protector = BlsContract.RequiresNotNull(protector, nameof(protector));
        _legacyProtector = legacyProtector;
        _filePath = BlsContract.RequiresNotNullOrWhiteSpace(filePath, nameof(filePath));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));

        // Asegurar directorio
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public bool TryGetApiKey(out string? key)
    {
        key = null;

        if (!File.Exists(_filePath))
            return false;

        try
        {
            var cipher = File.ReadAllBytes(_filePath);
            byte[] plain;
            var migrated = false;

            try
            {
                plain = _protector.Unprotect(cipher);
            }
            catch (System.Security.Cryptography.CryptographicException) when (_legacyProtector != null)
            {
                // API key guardada por una versión anterior con entropía fija:
                // se lee y se reencripta con la entropía derivada antes de
                // devolverla. El usuario no tiene que volver a pegarla (R-11).
                plain = _legacyProtector.Unprotect(cipher);
                migrated = true;
            }

            key = Encoding.UTF8.GetString(plain);

            if (migrated)
            {
                File.WriteAllBytes(_filePath, _protector.Protect(plain));
                _logger.Info("BlsSecureStore", "API key migrada a entropía derivada");
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException ||
                                    ex is IOException)
        {
            _logger.Warn("BlsSecureStore", "No se pudo descifrar la API key");
            throw new BlsSecureStoreException("Fallo al descifrar la API key. Vuelve a guardarla.", ex);
        }
    }

    public void SaveApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BlsInvalidParamsException("API key no puede ser null, vacío o solo whitespace");

        var plain = Encoding.UTF8.GetBytes(key);
        var cipher = _protector.Protect(plain);

        File.WriteAllBytes(_filePath, cipher);
        _logger.Info("BlsSecureStore", "API key guardada"); // sin valor
    }

    public void DeleteApiKey()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
            _logger.Info("BlsSecureStore", "API key eliminada");
        }
    }
}
