// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>Hallazgo de saneamiento: categoría y token, nunca el valor original.</summary>
public sealed record BlsPiiFinding(string Category, string Token);

/// <summary>Resultado de saneamiento: copia de los mensajes y hallazgos.</summary>
public sealed record BlsPiiScrubResult(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<BlsPiiFinding> Findings)
{
    public bool HasFindings => Findings != null && Findings.Count > 0;
}

/// <summary>
/// Sanea PII en los mensajes que salen hacia NIM (R-03 / Pilar 24). Trabaja
/// siempre sobre copias: el historial local conserva el contenido original.
/// Los hallazgos solo exponen categoría y token para no filtrar el dato.
/// </summary>
public static class BlsPiiScrubber
{
    private sealed class Rule
    {
        public Rule(string category, string token, string pattern)
        {
            Category = category;
            Token = token;
            Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public string Category { get; }
        public string Token { get; }
        public Regex Regex { get; }
    }

    // Orden importante: la connection string y los secretos se evalúan antes
    // que el correo o el teléfono para no partir una credencial en fragmentos.
    private static readonly Rule[] Rules =
    {
        new Rule(
            "CONNECTION_STRING",
            "[CONNECTION_STRING]",
            @"(?i)\b(?:Server|Data Source|Address)\s*=[^\r\n;]+;(?:[^\r\n;]*;){0,8}[^\r\n;]*\b(?:Password|Pwd|User ID|User Id|Uid)\s*=[^\r\n;]+"),
        new Rule(
            "REDACTED",
            "[REDACTED]",
            @"(?i)\b(?:nvapi-[A-Za-z0-9_\-]{8,}|(?:sk|pk|rk)-[A-Za-z0-9_\-]{16,})"),
        new Rule(
            "REDACTED",
            "[REDACTED]",
            @"(?i)\b(api[_-]?key|apikey|access[_-]?token|refresh[_-]?token|secret|password|pwd)\b\s*[:=]\s*[""']?[^\s,;""'}\]]+"),
        new Rule(
            "EMAIL",
            "[EMAIL]",
            @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b"),
        new Rule(
            "USER_PATH",
            "[USER_PATH]",
            @"(?i)\b[A-Z]:\\Users\\[^\\\s""']+"),
        new Rule(
            "IPV4",
            "[IPV4]",
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
        new Rule(
            "IBAN",
            "[IBAN]",
            @"\bES\d{22}\b"),
        new Rule(
            "NIF",
            "[NIF]",
            @"\b[0-9XYZ][0-9]{7}[A-Z]\b"),
        new Rule(
            "PHONE",
            "[PHONE]",
            @"(?<!\d)(?:\+?\d{1,3}[\s.\-]?)?(?:\(\d{2,4}\)[\s.\-]?)?\d{3,4}[\s.\-]\d{3,4}(?!\d)")
    };

    /// <summary>
    /// Devuelve una copia saneada de los mensajes. Idempotente: los tokens
    /// resultantes no vuelven a coincidir con ninguna regla.
    /// </summary>
    public static BlsPiiScrubResult Scrub(IReadOnlyList<ChatMessage> messages)
    {
        BlsContract.RequiresNotNull(messages, nameof(messages));

        var findings = new List<BlsPiiFinding>();
        var scrubbed = new List<ChatMessage>(messages.Count);

        foreach (var message in messages)
        {
            var content = ScrubText(message.Content ?? string.Empty, findings);
            IReadOnlyList<AssistantToolCall>? calls = null;

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                var list = new List<AssistantToolCall>(message.ToolCalls.Count);
                foreach (var call in message.ToolCalls)
                {
                    list.Add(new AssistantToolCall
                    {
                        Id = call.Id,
                        Type = call.Type,
                        Function = new AssistantToolCallFunction
                        {
                            Name = call.Function?.Name ?? string.Empty,
                            Arguments = ScrubText(call.Function?.Arguments ?? string.Empty, findings)
                        }
                    });
                }

                calls = list;
            }

            scrubbed.Add(new ChatMessage(message.Role, content)
            {
                ToolCallId = message.ToolCallId,
                ToolCalls = calls
            });
        }

        return new BlsPiiScrubResult(scrubbed, findings);
    }

    /// <summary>
    /// Solo detecta: útil para decidir si hace falta consentimiento sin
    /// construir la copia saneada. No registra los valores encontrados.
    /// </summary>
    public static IReadOnlyList<BlsPiiFinding> Detect(IReadOnlyList<ChatMessage> messages)
        => Scrub(messages).Findings;

    private static string ScrubText(string text, List<BlsPiiFinding> findings)
    {
        var value = text;
        foreach (var rule in Rules)
        {
            value = rule.Regex.Replace(value, _ =>
            {
                findings.Add(new BlsPiiFinding(rule.Category, rule.Token));
                return rule.Token;
            });
        }

        return value;
    }
}
