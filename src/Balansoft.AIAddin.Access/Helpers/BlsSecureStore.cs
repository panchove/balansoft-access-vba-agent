// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.Text;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// AlmacÃ©n seguro para la API key usando DPAPI (CurrentUser + entropÃ­a).
/// </summary>
public sealed class BlsSecureStore : IBlsSecureStore
{
    private readonly IDataProtector _protector;
    private readonly string _filePath;
    private readonly IBlsLogger _logger;

    public BlsSecureStore(
        IDataProtector protector,
        string filePath,
        IBlsLogger logger)
    {
        _protector = BlsContract.RequiresNotNull(protector, nameof(protector));
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
            var plain = _protector.Unprotect(cipher);
            key = Encoding.UTF8.GetString(plain);
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
            throw new BlsInvalidParamsException("API key no puede ser null, vacÃ­o o solo whitespace");

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
