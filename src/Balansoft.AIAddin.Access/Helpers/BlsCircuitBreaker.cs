// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 (Fiabilidad) / IEEE 7009-2024 (Fail-safe)
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Circuit breaker con umbral de fallos, ventana de apertura y un único sondeo
/// en HalfOpen. No usa temporizadores: evalúa el reloj inyectado en cada
/// llamada, lo que evita hilos en segundo plano y hace el test determinista.
/// </summary>
public sealed class BlsCircuitBreaker : IBlsCircuitBreaker
{
    private readonly object _gate = new object();
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly Func<DateTimeOffset> _clock;

    private int _failures;
    private BlsCircuitState _state;
    private DateTimeOffset _openedAt;
    private bool _probeInFlight;

    public BlsCircuitBreaker(
        int failureThreshold = 5,
        TimeSpan? openDuration = null,
        Func<DateTimeOffset>? clock = null)
    {
        BlsContract.RequiresRange(failureThreshold > 0, nameof(failureThreshold));
        BlsContract.RequiresRange(openDuration == null || openDuration.Value > TimeSpan.Zero, nameof(openDuration));

        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public BlsCircuitState State
    {
        get
        {
            lock (_gate)
            {
                return IsWindowElapsed() ? BlsCircuitState.HalfOpen : _state;
            }
        }
    }

    /// <inheritdoc />
    public bool AllowCall()
    {
        lock (_gate)
        {
            if (_state == BlsCircuitState.Open)
            {
                if (!IsWindowElapsed()) return false;

                _state = BlsCircuitState.HalfOpen;
                _probeInFlight = false;
            }

            if (_state == BlsCircuitState.HalfOpen)
            {
                if (_probeInFlight) return false;
                _probeInFlight = true;
            }

            return true;
        }
    }

    /// <inheritdoc />
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _probeInFlight = false;
            _state = BlsCircuitState.Closed;
        }
    }

    /// <inheritdoc />
    public void RecordFailure()
    {
        lock (_gate)
        {
            _probeInFlight = false;

            if (_state == BlsCircuitState.HalfOpen)
            {
                Trip();
                return;
            }

            _failures++;
            if (_failures >= _failureThreshold) Trip();
        }
    }

    private bool IsWindowElapsed()
    {
        return _state == BlsCircuitState.Open && _clock() - _openedAt >= _openDuration;
    }

    private void Trip()
    {
        _state = BlsCircuitState.Open;
        _openedAt = _clock();
    }
}

/// <summary>
/// Breaker inerte para los tests del comportamiento heredado: nunca bloquea
/// ni cambia de estado.
/// </summary>
public sealed class BlsNoOpCircuitBreaker : IBlsCircuitBreaker
{
    /// <summary>Instancia compartida y sin estado.</summary>
    public static BlsNoOpCircuitBreaker Instance { get; } = new BlsNoOpCircuitBreaker();

    /// <inheritdoc />
    public BlsCircuitState State => BlsCircuitState.Closed;

    /// <inheritdoc />
    public bool AllowCall() => true;

    /// <inheritdoc />
    public void RecordSuccess() { }

    /// <inheritdoc />
    public void RecordFailure() { }
}
