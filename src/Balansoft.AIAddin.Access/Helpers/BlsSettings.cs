// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-007 / spec 019

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balansoft.AIAddin.Helpers
{
    /// <summary>
    /// Preferencias no secretas del modelo. La API key NO va aqui: se guarda
    /// cifrada con DPAPI en BlsSecureStore.
    /// </summary>
    public sealed class BlsSettings
    {
        public const double DefaultTemperature = 0.6;
        public const int DefaultMaxTokens = 8192;
        public const int DefaultMaxToolIterations = 32;
        public const string DefaultLanguage = "es";

        public double Temperature { get; set; } = DefaultTemperature;
        public int MaxTokens { get; set; } = DefaultMaxTokens;
        public int MaxToolIterations { get; set; } = DefaultMaxToolIterations;
        public string? Endpoint { get; set; }
        public string Language { get; set; } = DefaultLanguage;

        /// <summary>Instancia global cargada al arrancar.</summary>
        public static BlsSettings Current { get; private set; } = new BlsSettings();

        /// <summary>Ruta del fichero de preferencias.</summary>
        public static string FilePath =>
            Path.Combine(BlsPaths.Root, "settings.json");

        /// <summary>Carga las preferencias; cualquier error deja los valores por defecto.</summary>
        public static BlsSettings Load(string? path = null)
        {
            var file = path ?? FilePath;
            var settings = new BlsSettings();
            try
            {
                if (!File.Exists(file)) return settings;
                var json = File.ReadAllText(file);
                var loaded = JsonSerializer.Deserialize<BlsSettings>(json, JsonOptions);
                if (loaded != null) settings = loaded;
            }
            catch (Exception)
            {
                // Preferencias corruptas: degradar a valores seguros.
            }
            settings.Clamp();
            return settings;
        }

        /// <summary>Guarda las preferencias en JSON legible (sin secretos).</summary>
        public void Save(string? path = null)
        {
            Clamp();
            var file = path ?? FilePath;
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(file, json);
        }

        /// <summary>Reemplaza la instancia global (se usa al guardar desde ajustes).</summary>
        public void Apply()
        {
            Clamp();
            Current = this;
            BlsStrings.CurrentLanguage = Language;
        }

        public void Clamp()
        {
            if (double.IsNaN(Temperature) || Temperature < 0.1 || Temperature > 1.0)
                Temperature = DefaultTemperature;
            if (MaxTokens < 256 || MaxTokens > 131072) MaxTokens = DefaultMaxTokens;
            if (MaxToolIterations < 1 || MaxToolIterations > 256) MaxToolIterations = DefaultMaxToolIterations;
            if (string.IsNullOrWhiteSpace(Language)) Language = DefaultLanguage;
            if (!string.IsNullOrWhiteSpace(Endpoint)) Endpoint = Endpoint!.Trim();
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
