// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Política de espera entre reintentos de NIM. Se inyecta para poder
/// verificar los delays sin esperas reales (skill 09).
/// </summary>
public interface IBlsNimRetryPolicy
{
    /// <summary>Delay antes del intento <paramref name="attempt"/> (base 0).</summary>
    TimeSpan NextDelay(int attempt);
}
