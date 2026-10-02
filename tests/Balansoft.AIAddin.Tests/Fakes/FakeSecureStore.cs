// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// Fake de IBlsSecureStore para tests.
/// </summary>
public sealed class FakeSecureStore : IBlsSecureStore
{
    private string? _apiKey;

    public bool TryGetApiKey(out string? key)
    {
        key = _apiKey;
        return !string.IsNullOrEmpty(_apiKey);
    }

    public void SaveApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BlsInvalidParamsException("API key no puede ser null, vacÃ­o o solo whitespace");
        _apiKey = key;
    }

    public void DeleteApiKey()
    {
        _apiKey = null;
    }

    public void SetApiKey(string? apiKey) => _apiKey = apiKey;
    public void Clear() => _apiKey = null;
}
