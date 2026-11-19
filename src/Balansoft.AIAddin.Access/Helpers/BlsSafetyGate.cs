// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IEEE 7009-2024 / ISO/IEC 25010:2023 / OWASP Agentic ASI03
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Kill switch determinista e independiente del razonamiento del modelo (Pilar 3, fail-safe).
/// Cuando está disparado, <see cref="EnsureAllowed"/> bloquea cualquier turno o tool call
/// antes de llegar al bridge. Solo una acción humana explícita puede <see cref="Reset"/>.
/// </summary>
public sealed class BlsSafetyGate
{
    private readonly object _sync = new object();
    private bool _tripped;
    private string _reason = string.Empty;

    /// <summary>Indica si el mecanismo de parada está activo.</summary>
    public bool IsTripped
    {
        get { lock (_sync) { return _tripped; } }
    }

    /// <summary>Motivo registrado del disparo; vacío si no está disparado.</summary>
    public string Reason
    {
        get { lock (_sync) { return _reason; } }
    }

    /// <summary>Dispara la parada segura. Operación idempotente.</summary>
    /// <param name="reason">Motivo humanamente legible del disparo.</param>
    public void Trip(string reason)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(reason, nameof(reason));
        lock (_sync)
        {
            _tripped = true;
            _reason = reason;
        }
    }

    /// <summary>Restaura el funcionamiento normal. Requiere intervención humana.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _tripped = false;
            _reason = string.Empty;
        }
    }

    /// <summary>
    /// Precondición de ejecución: lanza si el kill switch está disparado.
    /// </summary>
    /// <exception cref="BlsSafetyTrippedException">El gate está disparado.</exception>
    public void EnsureAllowed()
    {
        lock (_sync)
        {
            if (_tripped)
            {
                throw new BlsSafetyTrippedException(_reason);
            }
        }
    }
}

/// <summary>
/// Excepción lanzada cuando el kill switch impide una operación.
/// </summary>
[Serializable]
public sealed class BlsSafetyTrippedException : Exception
{
    public BlsSafetyTrippedException(string message) : base(message) { }
    public BlsSafetyTrippedException(string message, Exception inner) : base(message, inner) { }
}
