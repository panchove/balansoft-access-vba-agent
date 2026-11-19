// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

namespace Balansoft.AIAddin.Helpers;

/// <summary>Estados de un circuit breaker.</summary>
public enum BlsCircuitState
{
    /// <summary>Tráfico normal.</summary>
    Closed,

    /// <summary>El proveedor se considera caído; no se hacen llamadas.</summary>
    Open,

    /// <summary>Se permite un único sondeo para comprobar si se recuperó.</summary>
    HalfOpen
}

/// <summary>
/// Interruptor de circuito para el cliente NIM. Independiente del razonamiento
/// del modelo (Pilar 3) y con reloj inyectable para tests deterministas.
/// </summary>
public interface IBlsCircuitBreaker
{
    BlsCircuitState State { get; }

    /// <summary>Indica si se permite una llamada y registra el sondeo en HalfOpen.</summary>
    bool AllowCall();

    /// <summary>Registra una llamada correcta y cierra el circuito.</summary>
    void RecordSuccess();

    /// <summary>Registra un fallo y puede abrir el circuito.</summary>
    void RecordFailure();
}
