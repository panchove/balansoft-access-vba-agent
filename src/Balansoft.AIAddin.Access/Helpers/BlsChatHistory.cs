// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Balansoft.AIAddin.Helpers;

/// <summary>
/// Persistencia del historial de chat por proyecto (ADR-001).
/// Escritura atÃ³mica, debounce 500ms, rotaciÃ³n 1MB/5 archivos.
/// </summary>
public sealed class BlsChatHistory : IBlsChatHistory, IDisposable
{
    private readonly string _directory;
    private readonly IBlsLogger _logger;
    private readonly TimeSpan _debounce;
    private readonly JsonSerializerOptions _jsonOptions;

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
        BlsContract.RequiresNotNullOrWhiteSpace(projectKey, nameof(projectKey));

        var path = Path.Combine(_directory, $"{projectKey}.json");

        if (!File.Exists(path))
        {
            return Task.FromResult(new HistoryDocument { ProjectKey = projectKey });
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var doc = JsonSerializer.Deserialize<HistoryDocument>(json, _jsonOptions);
            if (doc is null || doc.Version != 1)
                throw new BlsHistorySchemaException(doc?.Version ?? -1);
            return Task.FromResult(doc!);
        }
        catch (JsonException ex)
        {
            RenameCorrupt(path);
            _logger.Warn("BlsChatHistory", $"Historial corrupto renombrado: {ex.Message}");
            return Task.FromResult(new HistoryDocument { ProjectKey = projectKey });
        }
        catch (BlsHistorySchemaException ex)
        {
            RenameCorrupt(path);
            _logger.Warn("BlsChatHistory", $"VersiÃ³n desconocida: {ex.Version}");
            return Task.FromResult(new HistoryDocument { ProjectKey = projectKey });
        }
        catch (IOException)
        {
            _logger.Warn("BlsChatHistory", "No se pudo leer el historial");
            return Task.FromResult(new HistoryDocument { ProjectKey = projectKey });
        }
    }

    public Task SaveAsync(string projectKey, HistoryDocument doc, CancellationToken ct)
    {
        doc.ProjectKey = projectKey;
        doc.UpdatedUtc = DateTime.UtcNow;

        lock (_pendingLock)
        {
            _pending[projectKey] = doc;
        }

        RestartTimer();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);

        List<(string key, HistoryDocument doc)> toWrite;
        lock (_pendingLock)
        {
            toWrite = new List<(string, HistoryDocument)>(_pending.Values.Select(d => (d.ProjectKey, d)));
            _pending.Clear();
        }

        foreach (var (key, doc) in toWrite)
        {
            try
            {
                WriteAtomic(key, doc);
            }
            catch (Exception ex)
            {
                _logger.Warn("BlsChatHistory", $"Error guardando historial de {key}: {ex.Message}", ex);
            }
        }

        return Task.CompletedTask;
    }

    public async Task ClearAsync(string projectKey, CancellationToken ct)
    {
        var path = Path.Combine(_directory, $"{projectKey}.json");
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            _logger.Info("BlsChatHistory", $"Historial de {projectKey} eliminado");
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", $"Error eliminando historial de {projectKey}", ex);
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
        // Ejecutar flush sin await (fire-and-forget)
        _ = FlushAsync(CancellationToken.None);
    }

    private void WriteAtomic(string projectKey, HistoryDocument doc)
    {
        var finalPath = Path.Combine(_directory, $"{projectKey}.json");
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

            _logger.Info("BlsChatHistory", $"Historial de {projectKey} guardado");
        }
        catch (Exception ex)
        {
            // Limpiar .tmp si existe
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { throw; }
            throw new BlsHistoryIOException($"Error guardando historial de {projectKey}", ex);
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
            var projectKey = Path.GetFileNameWithoutExtension(path);
            var archiveName = $"{projectKey}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            var archivePath = Path.Combine(dir, archiveName);

            File.Move(path, archivePath);
            _logger.Info("BlsChatHistory", $"Historial de {projectKey} archivado como {archiveName}");

            PruneOldArchives(projectKey);
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", "Error rotando historial", ex);
        }
    }

    private void PruneOldArchives(string projectKey)
    {
        try
        {
            var dir = _directory;
            var prefix = $"{projectKey}.";
            var archives = Directory.GetFiles(dir, $"{prefix}*.json")
                .Select(f => new { File = f, Date = ParseDateFromName(Path.GetFileName(f)) })
                .Where(x => x.Date.HasValue)
                .OrderBy(x => x.Date.Value)
                .ToList();

            while (archives.Count > 5)
            {
                var oldest = archives[0];
                File.Delete(oldest.File);
                archives.RemoveAt(0);
                _logger.Info("BlsChatHistory", $"Archivo archivado antiguo eliminado: {oldest.File}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("BlsChatHistory", "Error purgando archivados antiguos", ex);
        }
    }

    private static DateTime? ParseDateFromName(string fileName)
    {
        // Formato: {projectKey}.{yyyyMMdd-HHmmss}.json
        var parts = fileName.Split('.');
        if (parts.Length >= 3)
        {
            var datePart = parts[parts.Length - 2];
            if (DateTime.TryParseExact(datePart, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
        }
        return null;
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
        catch { /* best effort */ }
    }
}
