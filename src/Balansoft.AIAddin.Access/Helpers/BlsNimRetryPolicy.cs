// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 (Fiabilidad)
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Política fija heredada (1/2/4 s). Se usa solo en el constructor
/// <c>internal</c> de 7 argumentos para preservar los tests existentes.
/// </summary>
public sealed class BlsFixedRetryPolicy : IBlsNimRetryPolicy
{
    private readonly TimeSpan[] _delays;

    public BlsFixedRetryPolicy(TimeSpan[] delays)
    {
        BlsContract.RequiresNotNull(delays, nameof(delays));
        BlsContract.RequiresRange(delays.Length > 0, nameof(delays));
        _delays = (TimeSpan[])delays.Clone();
    }

    /// <inheritdoc />
    public TimeSpan NextDelay(int attempt)
    {
        BlsContract.RequiresRange(attempt >= 0, nameof(attempt));
        return _delays[Math.Min(attempt, _delays.Length - 1)];
    }
}

/// <summary>
/// Exponential backoff con *full jitter*:
/// <c>delay = random(0, min(max, base * 2^attempt))</c>.
/// El sorteo en todo el intervalo descorrelaciona a clientes que fallaron
/// al mismo tiempo (R-09).
/// </summary>
public sealed class BlsExponentialJitterRetryPolicy : IBlsNimRetryPolicy
{
    private static readonly Random SharedRandom = new Random();

    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Func<double> _random;

    public BlsExponentialJitterRetryPolicy(
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        Func<double>? random = null)
    {
        BlsContract.RequiresRange(baseDelay == null || baseDelay.Value > TimeSpan.Zero, nameof(baseDelay));
        BlsContract.RequiresRange(maxDelay == null || maxDelay.Value > TimeSpan.Zero, nameof(maxDelay));

        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _random = random ?? NextRandom;
    }

    /// <inheritdoc />
    public TimeSpan NextDelay(int attempt)
    {
        BlsContract.RequiresRange(attempt >= 0, nameof(attempt));

        var exponentialMs = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var windowMs = Math.Min(_maxDelay.TotalMilliseconds, exponentialMs);
        var factor = _random();

        BlsContract.Invariant(factor >= 0 && factor <= 1, "El jitter debe estar en [0, 1].");
        return TimeSpan.FromMilliseconds(windowMs * factor);
    }

    private static double NextRandom()
    {
        lock (SharedRandom)
        {
            return SharedRandom.NextDouble();
        }
    }
}
