// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / OpenTelemetry / Pilar 7 (calidad medible)
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Collections.Generic;
using System.Linq;

namespace Balansoft.AIAddin.Helpers;

/// <summary>Contrato opcional para componentes que exponen métricas SLO.</summary>
public interface IBlsSloAware
{
    /// <summary>Métricas del componente.</summary>
    BlsSloMetrics Metrics { get; }
}

/// <summary>Instantánea inmutable de los indicadores de nivel de servicio.</summary>
/// <param name="TotalWrites">Escrituras de auditoría intentadas.</param>
/// <param name="FailedWrites">Escrituras fallidas.</param>
/// <param name="WriteSuccessRate">Tasa de éxito de escritura (1.0 si no hay muestras).</param>
/// <param name="WriteP95Ms">Latencia p95 de escritura en milisegundos.</param>
/// <param name="TotalTurns">Turnos completados (o fallidos).</param>
/// <param name="FailedTurns">Turnos que terminaron con excepción.</param>
/// <param name="TurnSuccessRate">Tasa de éxito de turnos (1.0 si no hay muestras).</param>
/// <param name="ToolRejections">Tool calls rechazadas por fail-safe/HITL.</param>
/// <param name="IsWithinSlo">Indica si los tres objetivos se cumplen.</param>
public sealed record BlsSloSnapshot(
    int TotalWrites,
    int FailedWrites,
    double WriteSuccessRate,
    double WriteP95Ms,
    int TotalTurns,
    int FailedTurns,
    double TurnSuccessRate,
    int ToolRejections,
    bool IsWithinSlo);

/// <summary>
/// Métricas SLO en memoria, thread-safe y sin dependencias externas. No se
/// envían a ningún servidor (no hay telemetría remota): se exponen para el
/// diagnóstico y para los gates de CI.
/// </summary>
public sealed class BlsSloMetrics
{
    /// <summary>Objetivo mínimo de tasa de éxito de escritura.</summary>
    public const double MinWriteSuccessRate = 0.999;

    /// <summary>Objetivo máximo de latencia p95 de escritura (ms).</summary>
    public const double MaxWriteP95Ms = 50.0;

    /// <summary>Objetivo mínimo de tasa de éxito de turnos.</summary>
    public const double MinTurnSuccessRate = 0.95;

    private const int MaxSamples = 1000;

    private readonly object _sync = new();
    private readonly List<double> _writeLatencies = new();
    private int _totalWrites;
    private int _failedWrites;
    private int _totalTurns;
    private int _failedTurns;
    private int _toolRejections;

    /// <summary>Registra una escritura de log con su latencia y resultado.</summary>
    public void RecordWrite(double elapsedMs, bool success)
    {
        lock (_sync)
        {
            _totalWrites++;
            if (!success) _failedWrites++;
            if (_writeLatencies.Count >= MaxSamples) _writeLatencies.RemoveAt(0);
            _writeLatencies.Add(Math.Max(0.0, elapsedMs));
        }
    }

    /// <summary>Registra el desenlace de un turno.</summary>
    public void RecordTurn(bool success)
    {
        lock (_sync)
        {
            _totalTurns++;
            if (!success) _failedTurns++;
        }
    }

    /// <summary>Registra una tool call rechazada por fail-safe o HITL.</summary>
    public void RecordToolRejected()
    {
        lock (_sync)
        {
            _toolRejections++;
        }
    }

    /// <summary>Obtiene una instantánea coherente de todas las métricas.</summary>
    public BlsSloSnapshot Snapshot()
    {
        lock (_sync)
        {
            var writeRate = _totalWrites == 0
                ? 1.0
                : (_totalWrites - _failedWrites) / (double)_totalWrites;
            var turnRate = _totalTurns == 0
                ? 1.0
                : (_totalTurns - _failedTurns) / (double)_totalTurns;
            var p95 = ComputeP95(_writeLatencies);

            return new BlsSloSnapshot(
                _totalWrites,
                _failedWrites,
                writeRate,
                p95,
                _totalTurns,
                _failedTurns,
                turnRate,
                _toolRejections,
                writeRate >= MinWriteSuccessRate &&
                p95 <= MaxWriteP95Ms &&
                turnRate >= MinTurnSuccessRate);
        }
    }

    private static double ComputeP95(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0) return 0.0;
        var ordered = samples.OrderBy(x => x).ToList();
        var rank = (int)Math.Ceiling(ordered.Count * 0.95);
        return ordered[Math.Min(ordered.Count - 1, Math.Max(0, rank - 1))];
    }
}
