// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Códigos de error JSON-RPC reservados.
/// </summary>
public static class JsonRpcErrorCodes
{
    // JSON-RPC estándar
    public const int InvalidRequest   = -32600;
    public const int MethodNotFound   = -32601;
    public const int InvalidParams    = -32602;
    public const int InternalError    = -32603;

    // Aplicación (ADR-002, ADR-003)
    public const int ApplicationError = -32000;
    public const int FrameTooLarge    = -32001;
    public const int ToolTimeout      = -32002;
    public const int FrameIncomplete  = -32003;
    public const int Busy             = -32004;

    // Fail-safe / HITL en la frontera del bridge (Fase 1, auditoria 2026-11-22)
    public const int HitlDenied       = -32005;
    public const int SafetyTripped    = -32006;
    public const int ToolIterationLimit = -32007;
}
