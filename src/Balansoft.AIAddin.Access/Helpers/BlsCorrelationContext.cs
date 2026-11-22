// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: OpenTelemetry / ISO/IEC 27001 A.8.15 / EU AI Act Art. 12
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Threading;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Ámbito de correlación de un turno. Se guarda en <see cref="AsyncLocal{T}"/>,
/// por lo que fluye automáticamente a través de <c>await</c> hacia el cliente
/// NIM, el bridge y todas las tareas hijas del turno.
/// </summary>
public sealed class BlsCorrelationScope : IDisposable
{
    private readonly BlsCorrelationScope? _parent;
    private bool _disposed;

    internal BlsCorrelationScope(BlsCorrelationScope? parent, string correlationId, string traceId)
    {
        _parent = parent;
        CorrelationId = correlationId;
        TraceId = traceId;
    }

    /// <summary>Identificador del turno (una conversación usuario→respuesta).</summary>
    public string CorrelationId { get; }

    /// <summary>Identificador de la traza completa, compartido con el dispatcher del bridge.</summary>
    public string TraceId { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BlsCorrelationContext.SetCurrent(_parent);
    }
}

/// <summary>
/// Contexto de trazabilidad punta a punta (Pilar 20). Un único turno genera un
/// <c>correlation_id</c> y un <c>trace_id</c>; el primero se propaga por el
/// proceso mediante <see cref="AsyncLocal{T}"/> y el segundo se envía explícitamente
/// en la solicitud JSON-RPC para que el dispatcher lo registre con el mismo valor.
/// </summary>
public static class BlsCorrelationContext
{
    private static readonly AsyncLocal<BlsCorrelationScope?> CurrentScope = new();

    /// <summary>Ámbito activo en el contexto de ejecución actual, o <c>null</c>.</summary>
    public static BlsCorrelationScope? Current => CurrentScope.Value;

    /// <summary>Correlación activa, o cadena vacía si no hay ámbito.</summary>
    public static string CorrelationId => CurrentScope.Value?.CorrelationId ?? string.Empty;

    /// <summary>Traza activa, o cadena vacía si no hay ámbito.</summary>
    public static string TraceId => CurrentScope.Value?.TraceId ?? string.Empty;

    /// <summary>
    /// Abre un ámbito de correlación. Los ámbitos pueden anidarse; al disponer el
    /// hijo se restaura el padre. Los valores <c>null</c> se generan automáticamente.
    /// </summary>
    public static BlsCorrelationScope Begin(string? correlationId = null, string? traceId = null)
    {
        var scope = new BlsCorrelationScope(
            CurrentScope.Value,
            string.IsNullOrWhiteSpace(correlationId) ? NewId() : correlationId!,
            string.IsNullOrWhiteSpace(traceId) ? NewId() : traceId!);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Genera un identificador aleatorio de 128 bits sin guiones.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    internal static void SetCurrent(BlsCorrelationScope? scope) => CurrentScope.Value = scope;
}
