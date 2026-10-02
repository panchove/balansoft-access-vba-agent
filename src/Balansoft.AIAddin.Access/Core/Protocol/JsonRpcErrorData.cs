// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json.Serialization;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Datos extendidos del error JSON-RPC para diagnÃ³stico del modelo.
/// </summary>
/// <param name="Category">CategorÃ­a del error (ej. "VbeException", "ComException", "AccessException").</param>
/// <param name="Op">Nombre del mÃ©todo que fallÃ³ (ej. "write_module").</param>
/// <param name="HResult">HRESULT hexadecimal si es COMException (ej. "0x800A9C68").</param>
/// <param name="Stack">Stack trace completo solo en build Debug; null en Release.</param>
public sealed record JsonRpcErrorData(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("op")] string? Op,
    [property: JsonPropertyName("hresult")] string? HResult,
    [property: JsonPropertyName("stack")] string? Stack);
