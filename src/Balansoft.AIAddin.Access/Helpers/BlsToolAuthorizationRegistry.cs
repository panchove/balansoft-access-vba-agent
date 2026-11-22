// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IEEE 7009-2024 / EU AI Act Art. 14 / OWASP Agentic ASI02
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Registro de autorizaciones emitidas por la terminal despues de una
    /// aprobacion humana real. El dispatcher del bridge consume el token antes
    /// de ejecutar una tool Medium/High, de modo que un cliente directo del pipe
    /// no puede saltarse el HITL (Pilar 4 / ADR-015).
    /// </summary>
    /// <remarks>
    /// Los tokens son de un solo uso, caducan a los 60 s y se vinculan al
    /// metodo, al correlation_id del turno y al SHA-256 del payload. No se
    /// loguea el payload, solo su huella, ya calculada en
    /// <see cref="BlsApprovalRequest"/>.
    /// </remarks>
    public sealed class BlsToolAuthorizationRegistry
    {
        /// <summary>Validez de un token de aprobacion.</summary>
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

        private const int MaxEntries = 512;

        private sealed record Entry(
            string Method,
            string CorrelationId,
            string PayloadSha256,
            DateTimeOffset ExpiresAt,
            long Sequence);

        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        private readonly TimeSpan _ttl;
        private readonly Func<DateTimeOffset> _clock;
        private long _sequence;

        public BlsToolAuthorizationRegistry(TimeSpan? ttl = null, Func<DateTimeOffset>? clock = null)
        {
            BlsContract.RequiresRange(ttl == null || ttl.Value > TimeSpan.Zero, nameof(ttl));
            _ttl = ttl ?? DefaultTtl;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Emite un token tras una aprobacion humana. El valor devuelto es
        /// aleatorio de 128 bits y no contiene datos del payload.
        /// </summary>
        public string Issue(string method, string? correlationId, string? payloadSha256)
        {
            BlsContract.RequiresNotNullOrWhiteSpace(method, nameof(method));

            var token = NewToken();
            var now = _clock();
            _entries[token] = new Entry(
                method,
                correlationId ?? string.Empty,
                payloadSha256 ?? string.Empty,
                now.Add(_ttl),
                System.Threading.Interlocked.Increment(ref _sequence));

            Prune(now);
            BlsContract.Ensures(!string.IsNullOrWhiteSpace(token), "El token emitido no puede estar vacio.");
            BlsContract.Invariant(_entries.Count <= MaxEntries, "El registro no debe exceder su capacidad.");
            return token;
        }

        /// <summary>
        /// Consume y valida un token. Devuelve <c>false</c> si no existe,
        /// ha caducado o no coincide con la operacion solicitada. El consumo
        /// es siempre destructivo: un token nunca autoriza dos ejecuciones.
        /// </summary>
        public bool Validate(string? token, string method, string? correlationId, string? payloadSha256)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            BlsContract.RequiresNotNullOrWhiteSpace(method, nameof(method));

            if (!_entries.TryRemove(token, out var entry)) return false;
            if (entry.ExpiresAt <= _clock()) return false;

            return string.Equals(entry.Method, method, StringComparison.Ordinal)
                && string.Equals(entry.CorrelationId, correlationId ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(entry.PayloadSha256, payloadSha256 ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>Genera un token opaco de 128 bits.</summary>
        private static string NewToken()
        {
            var bytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private void Prune(DateTimeOffset now)
        {
            if (_entries.Count <= MaxEntries) return;

            foreach (var pair in _entries)
            {
                if (pair.Value.ExpiresAt <= now)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }

            if (_entries.Count <= MaxEntries) return;

            var victims = _entries
                .OrderBy(pair => pair.Value.Sequence)
                .Take(_entries.Count - MaxEntries)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var victim in victims)
            {
                _entries.TryRemove(victim, out _);
            }
        }
    }
}
