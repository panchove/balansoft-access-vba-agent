// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: ISO/IEC 25010:2023 / ISO/IEC 26514:2022
// Architecture Reference: ADR-001-PROJECT-IDENTITY.md / ADR-009-SECURE-STORE.md / ADR-013-SESSION-MANAGEMENT.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Persistencia del historial de chat por proyecto y sesiones según ADR-001, ADR-009 y ADR-013.
/// Soporta múltiples sesiones por proyecto, catálogo de sesiones, escritura atómica y retrocompatibilidad.
/// </summary>
public sealed class BlsChatHistory : IBlsChatHistory, IDisposable
{
    private readonly string _directory;
    private readonly IBlsLogger _logger;
    private readonly TimeSpan _debounce;
    private readonly JsonSerializerOptions _jsonOptions;

    // Cache de documentos pendientes de escritura (ProjectKey -> doc)
    private readonly Dictionary<string, HistoryDocument> _pending = new();
    private readonly object _pendingLock = new();
    private Timer? _timer;

    public BlsChatHistory(
        string directory,
        IBlsLogger logger,
        TimeSpan? debounce = null)
    {
        _directory = BlsContract.RequiresNotNull(directory, nameof(directory));
        _logger = BlsContract.RequiresNotNull(logger, nameof(logger));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);

        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        _timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task<HistoryDocument> LoadAsync(string projectKey, CancellationToken ct)
    {
        return LoadSessionAsync(projectKey, null, ct);
    }

    public async Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(string projectKey, CancellationToken ct)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));

        EnsureProjectMigrated(projectKey);

        var projectDir = Path.Combine(_directory, projectKey);
        if (!Directory.Exists(projectDir))
        {
            return Array.Empty<SessionMetadata>();
        }

        var sessionsFile = Path.Combine(projectDir, "sessions.json");
        if (!File.Exists(sessionsFile))
        {
            return Array.Empty<SessionMetadata>();
        }

        try
        {
            var json = File.ReadAllText(sessionsFile, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<SessionMetadata>>(json, _jsonOptions);
            return list?.OrderByDescending(s => s.UpdatedAtUtc).ToList() ?? (IReadOnlyList<SessionMetadata>)Array.Empty<SessionMetadata>();
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", $"Error leyendo catalogo de sesiones de {projectKey}: {ex.Message}");
            return Array.Empty<SessionMetadata>();
        }
    }

    public async Task<HistoryDocument> LoadSessionAsync(string projectKey, string? sessionId, CancellationToken ct)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));

        EnsureProjectMigrated(projectKey);

        var projectDir = Path.Combine(_directory, projectKey);

        // Si no se especificó sesión, consultar catálogo y elegir la más reciente
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var sessions = await ListSessionsAsync(projectKey, ct).ConfigureAwait(false);
            if (sessions.Count > 0)
            {
                sessionId = sessions[0].Id;
            }
            else
            {
                // Crear primera sesión por defecto
                return await CreateSessionAsync(projectKey, "Sesión inicial", ct).ConfigureAwait(false);
            }
        }

        var sessionPath = Path.Combine(projectDir, $"{sessionId}.json");
        if (!File.Exists(sessionPath))
        {
            // Si el archivo de sesión no existiera, crear una nueva
            return await CreateSessionAsync(projectKey, "Nueva sesión", ct).ConfigureAwait(false);
        }

        try
        {
            var json = File.ReadAllText(sessionPath, Encoding.UTF8);
            var doc = JsonSerializer.Deserialize<HistoryDocument>(json, _jsonOptions);
            if (doc is null || doc.Version != 1)
                throw new BlsHistorySchemaException(doc?.Version ?? -1);

            doc.ProjectKey = projectKey;
            doc.SessionId = sessionId;
            return doc;
        }
        catch (JsonException ex)
        {
            RenameCorrupt(sessionPath);
            _logger.Warn("BlsChatHistory", $"Sesión corrupta renombrada: {ex.Message}");
            return await CreateSessionAsync(projectKey, "Nueva sesión", ct).ConfigureAwait(false);
        }
        catch (BlsHistorySchemaException ex)
        {
            RenameCorrupt(sessionPath);
            _logger.Warn("BlsChatHistory", $"Versión desconocida en sesión: {ex.Version}");
            return await CreateSessionAsync(projectKey, "Nueva sesión", ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            _logger.Warn("BlsChatHistory", "No se pudo leer el archivo de sesión");
            return await CreateSessionAsync(projectKey, "Nueva sesión", ct).ConfigureAwait(false);
        }
    }

    public async Task<HistoryDocument> CreateSessionAsync(string projectKey, string? title, CancellationToken ct)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));

        var projectDir = Path.Combine(_directory, projectKey);
        if (!Directory.Exists(projectDir))
            Directory.CreateDirectory(projectDir);

        var sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N").Substring(0, 6);
        var effectiveTitle = string.IsNullOrWhiteSpace(title) ? "Nueva sesión" : title!;

        var doc = new HistoryDocument
        {
            ProjectKey = projectKey,
            SessionId = sessionId,
            Title = effectiveTitle,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        // Guardar documento de sesión
        var sessionPath = Path.Combine(projectDir, $"{sessionId}.json");
        var json = JsonSerializer.Serialize(doc, _jsonOptions);
        File.WriteAllText(sessionPath, json, Encoding.UTF8);

        // Actualizar catálogo de sesiones
        await UpdateSessionCatalogAsync(projectKey, new SessionMetadata
        {
            Id = sessionId,
            Title = effectiveTitle,
            CreatedAtUtc = doc.CreatedUtc,
            UpdatedAtUtc = doc.UpdatedUtc,
            MessageCount = 0
        }).ConfigureAwait(false);

        _logger.Info("BlsChatHistory", $"Nueva sesión creada: {sessionId} para proyecto {projectKey}");
        return doc;
    }

    public Task SaveAsync(string projectKey, HistoryDocument doc, CancellationToken ct)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));
        BlsContract.RequiresNotNull(doc, nameof(doc));

        doc.ProjectKey = projectKey;
        if (string.IsNullOrWhiteSpace(doc.SessionId))
        {
            doc.SessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        }
        doc.UpdatedUtc = DateTime.UtcNow;

        // Auto-generar título a partir del primer mensaje del usuario si conserva el título por defecto
        if (doc.Title == "Nueva sesión" || doc.Title == "Sesión inicial")
        {
            var firstUserMsg = doc.Messages.FirstOrDefault(m => m.Role == "user")?.Content;
            if (!string.IsNullOrWhiteSpace(firstUserMsg))
            {
                var clean = firstUserMsg!.Trim().Replace("\r", " ").Replace("\n", " ");
                doc.Title = clean.Length > 40 ? clean.Substring(0, 40) + "..." : clean;
            }
        }

        lock (_pendingLock)
        {
            // Clave única de guardado combinando proyecto y sesión
            var saveKey = $"{projectKey}::{doc.SessionId}";
            _pending[saveKey] = doc;
        }

        RestartTimer();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);

        List<HistoryDocument> toWrite;
        lock (_pendingLock)
        {
            toWrite = new List<HistoryDocument>(_pending.Values);
            _pending.Clear();
        }

        foreach (var doc in toWrite)
        {
            try
            {
                WriteAtomicSession(doc);
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsChatHistory", $"Error guardando sesión {doc.SessionId} de {doc.ProjectKey}: {ex.Message}", ex);
            }
        }

        return Task.CompletedTask;
    }

    public async Task ClearAsync(string projectKey, CancellationToken ct)
    {
        await DeleteSessionAsync(projectKey, null, ct).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(string projectKey, string? sessionId, CancellationToken ct)
    {
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));

        var projectDir = Path.Combine(_directory, projectKey);
        var legacyFile = Path.Combine(_directory, $"{projectKey}.json");

        if (string.IsNullOrEmpty(sessionId))
        {
            // Borrar todo el historial y sesiones del proyecto
            try
            {
                if (Directory.Exists(projectDir))
                    Directory.Delete(projectDir, recursive: true);
                if (File.Exists(legacyFile))
                    File.Delete(legacyFile);
                _logger.Info("BlsChatHistory", $"Historial completo de {projectKey} eliminado");
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsChatHistory", $"Error eliminando historial completo de {projectKey}", ex);
            }
        }
        else
        {
            // Borrar sesión específica
            var sessionPath = Path.Combine(projectDir, $"{sessionId}.json");
            try
            {
                if (File.Exists(sessionPath))
                    File.Delete(sessionPath);

                // Remover del catálogo
                var sessionsFile = Path.Combine(projectDir, "sessions.json");
                if (File.Exists(sessionsFile))
                {
                    var json = File.ReadAllText(sessionsFile, Encoding.UTF8);
                    var list = JsonSerializer.Deserialize<List<SessionMetadata>>(json, _jsonOptions) ?? new();
                    list.RemoveAll(s => s.Id == sessionId);
                    File.WriteAllText(sessionsFile, JsonSerializer.Serialize(list, _jsonOptions), Encoding.UTF8);
                }

                _logger.Info("BlsChatHistory", $"Sesión {sessionId} eliminada de {projectKey}");
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsChatHistory", $"Error eliminando sesión {sessionId}", ex);
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void RestartTimer()
    {
        _timer?.Change((int)_debounce.TotalMilliseconds, Timeout.Infinite);
    }

    private void OnTimerElapsed(object? state)
    {
        _ = FlushAsync(CancellationToken.None);
    }

    private void WriteAtomicSession(HistoryDocument doc)
    {
        var projectDir = Path.Combine(_directory, doc.ProjectKey);
        if (!Directory.Exists(projectDir))
            Directory.CreateDirectory(projectDir);

        var finalPath = Path.Combine(projectDir, $"{doc.SessionId}.json");
        var tmpPath = finalPath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(doc, _jsonOptions);
            File.WriteAllText(tmpPath, json, Encoding.UTF8);

            RotateIfNeeded(finalPath);

            if (File.Exists(finalPath))
            {
                File.Replace(tmpPath, finalPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmpPath, finalPath);
            }

            // Actualizar catálogo de sesiones
            _ = UpdateSessionCatalogAsync(doc.ProjectKey, new SessionMetadata
            {
                Id = doc.SessionId,
                Title = doc.Title,
                CreatedAtUtc = doc.CreatedUtc,
                UpdatedAtUtc = doc.UpdatedUtc,
                MessageCount = doc.Messages.Count
            });

            _logger.Info("BlsChatHistory", $"Sesión {doc.SessionId} de {doc.ProjectKey} guardada");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw new BlsHistoryIOException($"Error guardando sesión de {doc.ProjectKey}", ex);
        }
    }

    private async Task UpdateSessionCatalogAsync(string projectKey, SessionMetadata meta)
    {
        var projectDir = Path.Combine(_directory, projectKey);
        if (!Directory.Exists(projectDir))
            Directory.CreateDirectory(projectDir);

        var sessionsFile = Path.Combine(projectDir, "sessions.json");
        List<SessionMetadata> list;

        try
        {
            if (File.Exists(sessionsFile))
            {
                var json = File.ReadAllText(sessionsFile, Encoding.UTF8);
                list = JsonSerializer.Deserialize<List<SessionMetadata>>(json, _jsonOptions) ?? new();
            }
            else
            {
                list = new List<SessionMetadata>();
            }

            list.RemoveAll(s => s.Id == meta.Id);
            list.Insert(0, meta);

            File.WriteAllText(sessionsFile, JsonSerializer.Serialize(list, _jsonOptions), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", $"Error actualizando catálogo de sesiones: {ex.Message}");
        }
    }

    private void EnsureProjectMigrated(string projectKey)
    {
        var legacyPath = Path.Combine(_directory, $"{projectKey}.json");
        var projectDir = Path.Combine(_directory, projectKey);

        if (File.Exists(legacyPath) && !Directory.Exists(projectDir))
        {
            try
            {
                Directory.CreateDirectory(projectDir);
                var json = File.ReadAllText(legacyPath, Encoding.UTF8);
                var doc = JsonSerializer.Deserialize<HistoryDocument>(json, _jsonOptions);

                if (doc != null)
                {
                    var sessionId = "legacy_session";
                    doc.SessionId = sessionId;
                    doc.ProjectKey = projectKey;
                    if (string.IsNullOrWhiteSpace(doc.Title))
                    {
                        var firstUser = doc.Messages.FirstOrDefault(m => m.Role == "user")?.Content;
                        doc.Title = !string.IsNullOrWhiteSpace(firstUser)
                            ? (firstUser!.Length > 35 ? firstUser.Substring(0, 35) + "..." : firstUser)
                            : "Sesión inicial";
                    }

                    var sessionFile = Path.Combine(projectDir, $"{sessionId}.json");
                    File.WriteAllText(sessionFile, JsonSerializer.Serialize(doc, _jsonOptions), Encoding.UTF8);

                    var catalog = new List<SessionMetadata>
                    {
                        new SessionMetadata
                        {
                            Id = sessionId,
                            Title = doc.Title,
                            CreatedAtUtc = doc.CreatedUtc == default ? doc.UpdatedUtc : doc.CreatedUtc,
                            UpdatedAtUtc = doc.UpdatedUtc,
                            MessageCount = doc.Messages.Count
                        }
                    };
                    File.WriteAllText(Path.Combine(projectDir, "sessions.json"), JsonSerializer.Serialize(catalog, _jsonOptions), Encoding.UTF8);
                    _logger.Info("BlsChatHistory", $"Historial legado de {projectKey} migrado a estructura multi-sesión");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsChatHistory", $"Aviso al migrar historial legado de {projectKey}: {ex.Message}");
            }
        }
    }

    private void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var info = new FileInfo(path);
            if (info.Length < 1024 * 1024) // 1 MB
                return;

            var dir = Path.GetDirectoryName(path)!;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
            var archiveName = $"{nameWithoutExt}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            var archivePath = Path.Combine(dir, archiveName);

            File.Move(path, archivePath);
            _logger.Info("BlsChatHistory", $"Sesión archivada como {archiveName}");
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", "Error rotando archivo de sesión", ex);
        }
    }

    private static void RenameCorrupt(string path)
    {
        try
        {
            var corruptPath = path + ".corrupt";
            if (File.Exists(corruptPath))
                File.Delete(corruptPath);
            File.Move(path, corruptPath);
        }
        catch { }
    }
}
