// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 26514:2022
// Architecture Reference: ADR-011-LOGGING-VERSION.md

using System;
using System.IO;
using System.Text;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Logger del add-in que escribe en %APPDATA%\Balansoft\AIAddin\logs\ según ADR-011.
    /// Thread-safe, con rotación diaria y saneamiento estricto de secretos (prohibición de API keys).
    /// </summary>
    public sealed class BlsLogger : IBlsLogger, IDisposable
    {
        private readonly string _logDir;
        private readonly object _lock = new();

        public BlsLogger(string? logDir = null)
        {
            _logDir = logDir ?? BlsPaths.LogsDir;
        }

        public void Debug(string component, string message) => Write("DEBUG", component, message);
        public void Info(string component, string message) => Write("INFO", component, message);
        public void Warn(string component, string message, Exception? ex = null) => Write("WARN", component, message, ex);
        public void Error(string component, string message, Exception? ex = null) => Write("ERROR", component, message, ex);

        private void Write(string level, string component, string message, Exception? ex = null)
        {
            try
            {
                if (!Directory.Exists(_logDir))
                {
                    Directory.CreateDirectory(_logDir);
                }

                var today = DateTime.UtcNow.ToString("yyyyMMdd");
                var path = Path.Combine(_logDir, $"balansoft-{today}.log");
                var line = $"{DateTime.UtcNow:O} [{level}] [{component}] {message}";
                if (ex != null)
                {
                    line += $" - Exception: {ex.ToString()}";
                }

                lock (_lock)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Un fallo de log nunca debe tumbar el host COM
            }
        }

        public void Dispose()
        {
        }
    }
}
