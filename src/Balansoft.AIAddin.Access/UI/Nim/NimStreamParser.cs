// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Acumula tool_calls fragmentados desde chunks SSE y los emite al sink.
/// </summary>
internal sealed class NimStreamParser
{
    private readonly IBlsNimSink _sink;
    private readonly Dictionary<int, ToolCallAccumulator> _accumulators = new Dictionary<int, ToolCallAccumulator>();
    private bool _completed;

    public NimStreamParser(IBlsNimSink sink)
    {
        _sink = sink ?? throw new System.ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// Procesa una línea SSE (sin el prefijo "data:") ni el centinela [DONE].
    /// </summary>
    public void Feed(string dataLine)
    {
        if (string.IsNullOrWhiteSpace(dataLine)) return;
        if (dataLine == "[DONE]") return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(dataLine);
        }
        catch (JsonException ex)
        {
            throw new BlsNimProtocolException(
                "Línea SSE con JSON inválido",
                Truncate(dataLine, 2048),
                ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
                return;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)) return;

            EmitContent(delta);
            AccumulateToolCalls(delta);
        }
    }

    private void EmitContent(JsonElement delta)
    {
        if (!delta.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.String) return;

        var text = content.GetString();
        if (!string.IsNullOrEmpty(text))
            _sink.OnTextChunk(text);
    }

    private void AccumulateToolCalls(JsonElement delta)
    {
        if (!delta.TryGetProperty("tool_calls", out var toolCalls)) return;
        if (toolCalls.ValueKind != JsonValueKind.Array) return;

        foreach (var tc in toolCalls.EnumerateArray())
        {
            if (!tc.TryGetProperty("index", out var idx) || idx.ValueKind != JsonValueKind.Number) continue;
            var index = idx.GetInt32();

            if (!_accumulators.TryGetValue(index, out var acc))
            {
                acc = new ToolCallAccumulator();
                _accumulators[index] = acc;
            }

            if (tc.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                acc.Id = id.GetString();

            if (tc.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                acc.Type = type.GetString();

            if (tc.TryGetProperty("function", out var func))
            {
                if (func.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    acc.Name = name.GetString();

                if (func.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                    acc.Arguments += args.GetString();
            }
        }
    }

    /// <summary>
    /// Emite las tool calls acumuladas en orden de index. Idempotente: las
    /// llamadas ya emitidas no se repiten aunque se invoque más de una vez.
    /// </summary>
    public void Complete()
    {
        if (_completed) return;
        _completed = true;

        var ordered = new List<KeyValuePair<int, ToolCallAccumulator>>(_accumulators);
        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));

        foreach (var kvp in ordered)
        {
            var acc = kvp.Value;

            if (string.IsNullOrEmpty(acc.Id) || string.IsNullOrEmpty(acc.Name))
                continue;

            try
            {
                using (JsonDocument.Parse(acc.Arguments))
                {
                }
            }
            catch (JsonException ex)
            {
                _sink.OnError(new BlsNimProtocolException(
                    $"tool_call {acc.Id} con argumentos JSON inválido",
                    Truncate(acc.Arguments, 2048),
                    ex));
                continue;
            }

            _sink.OnToolCall(new ToolCallComplete(acc.Id, acc.Name, acc.Arguments));
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value.Substring(0, max) + "â€¦ [truncado]";
    }

    /// <summary>
    /// Acumulador interno para un tool_call por index.
    /// </summary>
    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Arguments { get; set; } = string.Empty;
    }
}

