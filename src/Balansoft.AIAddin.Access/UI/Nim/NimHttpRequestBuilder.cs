// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Construye el payload HTTP para la API NIM (esquema OpenAI chat completions).
/// </summary>
internal static class NimHttpRequestBuilder
{
    /// <summary>
    /// Modelo por defecto si no se pasa parámetro ni hay NIM_MODEL en el entorno.
    /// </summary>
    public const string DefaultModel = "nvidia/nemotron-3-ultra-550b-a55b";

    private const double Temperature = 0.2;
    private const int MaxTokens = 8192;

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Serializa el cuerpo JSON de la petición POST.
    /// Los headers (Authorization, Accept) los fija <see cref="BlsNimClient"/> sobre
    /// el <c>HttpRequestMessage</c>, no sobre el contenido.
    /// </summary>
    public static string BuildJson(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools)
    {
        var hasTools = tools != null && tools.Count > 0;

        var payload = new
        {
            model = model,
            messages = messages ?? new List<ChatMessage>(),
            tools = hasTools ? tools.Select(Wrap).ToList() : (object)null,
            tool_choice = hasTools ? "auto" : (object)null,
            stream = true,
            temperature = Temperature,
            max_tokens = MaxTokens
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    /// <summary>
    /// Envuelve una tool en el sobre { type, function } que exige la API.
    /// </summary>
    private static ToolRequestEnvelope Wrap(ToolDefinition tool)
    {
        return new ToolRequestEnvelope
        {
            Type = "function",
            Function = new ToolRequestFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.ParametersSchema
            }
        };
    }

    /// <summary>
    /// Resuelve el modelo con prioridad: parámetro &gt; env &gt; default.
    /// </summary>
    public static string ResolveModel(string model)
    {
        if (!string.IsNullOrEmpty(model))
            return model;

        var env = Environment.GetEnvironmentVariable("NIM_MODEL");
        if (!string.IsNullOrEmpty(env))
            return env;

        return DefaultModel;
    }

    internal sealed class ToolRequestEnvelope
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("function")]
        public ToolRequestFunction Function { get; set; }
    }

    internal sealed class ToolRequestFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public JsonElement Parameters { get; set; }
    }
}

