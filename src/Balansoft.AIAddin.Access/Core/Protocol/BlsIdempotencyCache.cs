// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 (Fiabilidad)
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Caché acotada y con TTL para respuestas idempotentes. Caducidad y evicción
/// se comprueban con un reloj inyectable, de modo que los tests no necesiten
/// esperas reales (skill 09).
/// </summary>
public sealed class BlsIdempotencyCache : IBlsIdempotencyCache
{
    private const int DefaultMaxEntries = 256;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private sealed record Entry(JsonRpcResponse Response, DateTimeOffset ExpiresAt, long Sequence);

    private readonly ConcurrentDictionary<string, Entry> _entries =
        new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly Func<DateTimeOffset> _clock;
    private long _sequence;

    public BlsIdempotencyCache(
        TimeSpan? ttl = null,
        int maxEntries = DefaultMaxEntries,
        Func<DateTimeOffset>? clock = null)
    {
        BlsContract.RequiresRange(ttl == null || ttl.Value > TimeSpan.Zero, nameof(ttl));
        BlsContract.RequiresRange(maxEntries > 0, nameof(maxEntries));

        _ttl = ttl ?? DefaultTtl;
        _maxEntries = maxEntries;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool TryGet(string key, out JsonRpcResponse? response)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(key, nameof(key));

        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > _clock())
            {
                response = entry.Response;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        response = null;
        return false;
    }

    /// <inheritdoc />
    public void Store(string key, JsonRpcResponse response)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(key, nameof(key));
        BlsContract.RequiresNotNull(response, nameof(response));

        var now = _clock();
        _entries[key] = new Entry(response, now.Add(_ttl), Interlocked.Increment(ref _sequence));

        if (_entries.Count <= _maxEntries) return;

        var victims = _entries
            .OrderBy(pair => pair.Value.Sequence)
            .Take(_entries.Count - _maxEntries)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var victim in victims)
        {
            _entries.TryRemove(victim, out _);
        }
    }
}
