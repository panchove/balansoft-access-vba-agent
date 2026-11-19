// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Caché en memoria de respuestas de tools de escritura, por clave de
/// idempotencia. Su única finalidad es evitar la doble ejecución de un frame
/// reenviado (R-06).
/// </summary>
public interface IBlsIdempotencyCache
{
    /// <summary>Devuelve la respuesta cacheada si la clave está vigente.</summary>
    bool TryGet(string key, out JsonRpcResponse? response);

    /// <summary>Almacena la respuesta de una escritura ejecutada una vez.</summary>
    void Store(string key, JsonRpcResponse response);
}
