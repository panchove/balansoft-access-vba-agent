// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IETF AAT / EU AI Act Art. 12 / ISO/IEC 27001 A.8.15 / ISO/IEC 42001
// Architecture Reference: ADR-011-LOGGING-VERSION.md / ADR-016-AUDIT-LOGGING.md

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Logger de auditoría del add-in. Escribe JSON Lines (un evento por línea)
    /// en <c>%APPDATA%\Balansoft\AIAddin\logs\balansoft-yyyyMMdd.log</c> con
    /// <c>correlation_id</c>, <c>trace_id</c> y una cadena SHA-256 enlazada
    /// (<c>prev_hash + hash</c>) verificable con <see cref="BlsAuditChain.Verify"/>.
    /// El fichero es append-only: nunca se trunca ni se reescribe.
    /// </summary>
    /// <remarks>
    /// Un fallo de escritura NO se propaga (spec 008 R008): perder un evento es
    /// preferible a tumbar el host COM. El fallo se contabiliza en
    /// <see cref="Metrics"/> para que el SLO lo refleje.
    /// </remarks>
    public sealed class BlsLogger : IBlsLogger, IBlsSloAware, IDisposable
    {
        private const int SchemaVersion = 1;

        private readonly string _logDir;
        private readonly object _sync = new object();
        private bool _disposed;

        /// <summary>Métricas SLO del logger.</summary>
        public BlsSloMetrics Metrics { get; } = new();

        /// <summary>Ultimo error de escritura; nunca contiene datos del evento.</summary>
        public Exception? LastWriteError { get; private set; }

        /// <summary>Crea el logger. El directorio se crea en la primera escritura.</summary>
        /// <param name="logDir">Directorio inyectable para tests; <c>null</c> usa <see cref="BlsPaths.LogsDir"/>.</param>
        public BlsLogger(string? logDir = null)
        {
            _logDir = string.IsNullOrWhiteSpace(logDir) ? BlsPaths.LogsDir : logDir!;
        }

        public void Debug(string component, string message) => Write("DEBUG", component, message, null);
        public void Info(string component, string message) => Write("INFO", component, message, null);
        public void Warn(string component, string message, Exception? ex = null) => Write("WARN", component, message, ex);
        public void Error(string component, string message, Exception? ex = null) => Write("ERROR", component, message, ex);

        private void Write(string level, string component, string message, Exception? ex)
        {
            if (_disposed) return;

            var stopwatch = Stopwatch.StartNew();
            var succeeded = false;
            try
            {
                Directory.CreateDirectory(_logDir);

                var path = Path.Combine(
                    _logDir,
                    BlsAuditChain.FileNameFor(DateTime.UtcNow));

                lock (_sync)
                {
                    if (!BlsAuditChain.TryReadLastHash(path, out var lastHash))
                    {
                        // Cadena corrupta: no escribir encima. El evento se pierde,
                        // pero no se falsifica la auditoría (fail-safe).
                        return;
                    }

                    // Fase 2: si el fichero de hoy aun no tiene eventos, la
                    // semilla es el ultimo hash del dia anterior. Borrar un
                    // dia entero rompe la cadena y VerifyDirectory lo detecta.
                    var prevHash = string.IsNullOrEmpty(lastHash)
                        ? BlsAuditChain.SeedFor(_logDir, DateTime.UtcNow)
                        : lastHash!;

                    var correlationId = BlsCorrelationContext.CorrelationId;
                    var traceId = BlsCorrelationContext.TraceId;
                    if (string.IsNullOrEmpty(correlationId)) correlationId = BlsCorrelationContext.NewId();
                    if (string.IsNullOrEmpty(traceId)) traceId = BlsCorrelationContext.NewId();

                    var timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    var detail = ex == null ? null : BlsLogSanitizer.DescribeException(ex);
                    var payload = BuildPayload(
                        timestamp, level, component, message,
                        correlationId, traceId, detail, prevHash);

                    var hash = BlsAuditChain.ComputeHash(prevHash, payload);
                    var line = payload.Substring(0, payload.Length - 1) + ",\"hash\":\"" + hash + "\"}";

                    using var stream = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.WriteLine(line);
                    writer.Flush();
                }

                succeeded = true;
            }
            catch (Exception writeEx)
            {
                // Un fallo de log nunca debe tumbar el host COM (spec 008 R008),
                // pero el error queda expuesto para que el arranque pueda
                // disparar el fail-safe (Fase 2).
                LastWriteError = writeEx;
            }
            finally
            {
                stopwatch.Stop();
                Metrics.RecordWrite(stopwatch.Elapsed.TotalMilliseconds, succeeded);
            }
        }

        /// <summary>
        /// Serializa el payload canónico SIN el campo <c>hash</c>, con orden de
        /// claves estable para que la verificación sea determinista.
        /// </summary>
        private static string BuildPayload(
            string timestamp,
            string level,
            string component,
            string message,
            string correlationId,
            string traceId,
            BlsExceptionDetail? detail,
            string prevHash)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                json.WriteNumber("schema_version", SchemaVersion);
                json.WriteString("ts", timestamp);
                json.WriteString("level", level);
                json.WriteString("component", component);
                json.WriteString("message", BlsLogSanitizer.Sanitize(message));
                json.WriteString("correlation_id", correlationId);
                json.WriteString("trace_id", traceId);

                if (detail != null)
                {
                    json.WriteStartObject("exception");
                    json.WriteString("type", detail.Type);
                    json.WriteString("message", detail.Message);
                    if (detail.HResult != null) json.WriteString("hresult", detail.HResult);
                    json.WriteEndObject();
                }

                json.WriteString("prev_hash", prevHash);
                json.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Verifica la cadena completa de todos los dias. Al arranque, un
        /// resultado invalido debe disparar el kill switch antes de cualquier
        /// tool call (Fase 2, CS-009).
        /// </summary>
        public BlsAuditVerificationResult VerifyChain() => BlsAuditChain.VerifyDirectory(_logDir);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
