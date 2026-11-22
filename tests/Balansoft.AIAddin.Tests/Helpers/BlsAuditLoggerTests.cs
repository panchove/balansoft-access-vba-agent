// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IETF AAT / EU AI Act Art. 12 / ISO/IEC/IEEE 29119
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

/// <summary>
/// F3 / R-04: el log debe ser JSON estructurado, append-only y verificable.
/// Todos los tests usan un directorio temporal (nunca %APPDATA% real).
/// </summary>
[TestClass]
public sealed class BlsAuditLoggerTests
{
    private string _logDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "bls-audit-tests-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true);
    }

    private string LogPath => Path.Combine(
        _logDir,
        $"balansoft-{DateTime.UtcNow:yyyyMMdd}.log");

    private static JsonDocument ParseFirstEvent(string path)
    {
        var first = File.ReadLines(path).First(line => !string.IsNullOrWhiteSpace(line));
        return JsonDocument.Parse(first);
    }

    [TestMethod]
    public void Logger_EscribeJsonConCamposObligatorios()
    {
        using var logger = new BlsLogger(_logDir);

        logger.Info("BlsPrueba", "evento de prueba");

        using var doc = ParseFirstEvent(LogPath);
        var root = doc.RootElement;
        Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
        Assert.AreEqual("INFO", root.GetProperty("level").GetString());
        Assert.AreEqual("BlsPrueba", root.GetProperty("component").GetString());
        Assert.AreEqual("evento de prueba", root.GetProperty("message").GetString());
        Assert.AreEqual(32, root.GetProperty("correlation_id").GetString()!.Length);
        Assert.AreEqual(32, root.GetProperty("trace_id").GetString()!.Length);
        Assert.AreEqual(BlsAuditChain.GenesisHash, root.GetProperty("prev_hash").GetString());
        Assert.AreEqual(64, root.GetProperty("hash").GetString()!.Length);
        Assert.IsTrue(DateTime.TryParse(root.GetProperty("ts").GetString(), out _));
    }

    [TestMethod]
    public void Logger_CadenaUnEvento_EsVerificable()
    {
        using var logger = new BlsLogger(_logDir);
        logger.Info("A", "uno");

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsTrue(result.IsValid, result.Reason);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Logger_VariosEventos_PrevHashEncadenado()
    {
        using var logger = new BlsLogger(_logDir);
        logger.Debug("A", "uno");
        logger.Info("B", "dos");
        logger.Warn("C", "tres");
        logger.Error("D", "cuatro", new InvalidOperationException("fallo"));

        var lines = File.ReadAllLines(LogPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.AreEqual(4, lines.Count);

        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        Assert.AreEqual(
            first.RootElement.GetProperty("hash").GetString(),
            second.RootElement.GetProperty("prev_hash").GetString());

        var verification = BlsAuditChain.Verify(LogPath);
        Assert.IsTrue(verification.IsValid, verification.Reason);
        Assert.AreEqual(4, verification.Count);
    }

    [TestMethod]
    public void Logger_ConScope_UsaCorrelationDelTurno()
    {
        using var logger = new BlsLogger(_logDir);
        using (BlsCorrelationContext.Begin("corr-turno", "traza-turno"))
        {
            logger.Info("BlsNimTerminal", "dentro del turno");
        }
        logger.Info("BlsNimTerminal", "fuera del turno");

        var lines = File.ReadAllLines(LogPath);
        using var inside = JsonDocument.Parse(lines[0]);
        using var outside = JsonDocument.Parse(lines[1]);
        Assert.AreEqual("corr-turno", inside.RootElement.GetProperty("correlation_id").GetString());
        Assert.AreEqual("traza-turno", inside.RootElement.GetProperty("trace_id").GetString());
        Assert.AreNotEqual("corr-turno", outside.RootElement.GetProperty("correlation_id").GetString());
    }

    [TestMethod]
    public void Logger_ErrorConExcepcion_RegistraTipoYMensaje()
    {
        using var logger = new BlsLogger(_logDir);
        logger.Error("X", "fallo", new InvalidOperationException("detalle del fallo"));

        using var doc = ParseFirstEvent(LogPath);
        var exception = doc.RootElement.GetProperty("exception");
        Assert.AreEqual("InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.AreEqual("detalle del fallo", exception.GetProperty("message").GetString());
        Assert.IsFalse(exception.TryGetProperty("stack", out _));
    }

    [TestMethod]
    public void Logger_MensajeConSecretos_SaleSaneado()
    {
        using var logger = new BlsLogger(_logDir);
        logger.Info("X", "NIM_API_KEY=nvapi-abcdefgh12345678 Bearer abcdefgh12345678 password: supersecreto");

        using var doc = ParseFirstEvent(LogPath);
        var message = doc.RootElement.GetProperty("message").GetString()!;
        Assert.IsFalse(message.Contains("nvapi-abcdefgh12345678"));
        Assert.IsFalse(message.Contains("abcdefgh12345678"));
        Assert.IsFalse(message.Contains("supersecreto"));
        Assert.IsTrue(BlsAuditChain.Verify(LogPath).IsValid);
    }

    [TestMethod]
    public void Logger_AppendOnly_NoTruncaFicheroPrevio()
    {
        using (var first = new BlsLogger(_logDir)) first.Info("A", "inicio");
        var antes = File.ReadAllText(LogPath);

        using (var second = new BlsLogger(_logDir)) second.Info("A", "continuacion");
        var despues = File.ReadAllText(LogPath);

        StringAssert.StartsWith(despues, antes);
        Assert.AreEqual(2, despues.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l)));
        Assert.IsTrue(BlsAuditChain.Verify(LogPath).IsValid);
    }

    [TestMethod]
    public async Task Logger_CienEscriturasConcurrentes_CadenaValida()
    {
        using var logger = new BlsLogger(_logDir);

        var writes = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            logger.Info("Concurrente", $"evento-{i}")));
        await Task.WhenAll(writes);

        Assert.AreEqual(100, File.ReadAllLines(LogPath).Count(l => !string.IsNullOrWhiteSpace(l)));
        var verification = BlsAuditChain.Verify(LogPath);
        Assert.IsTrue(verification.IsValid, verification.Reason);
        Assert.AreEqual(100, verification.Count);
    }

    [TestMethod]
    public void Logger_FalloDeEscritura_NoPropagaYContabilizaSlo()
    {
        var archivo = Path.Combine(_logDir, "no-es-un-directorio.txt");
        Directory.CreateDirectory(_logDir);
        File.WriteAllText(archivo, "contenido");
        using var logger = new BlsLogger(archivo);

        logger.Info("X", "no se puede escribir");

        var snapshot = logger.Metrics.Snapshot();
        Assert.AreEqual(1, snapshot.TotalWrites);
        Assert.AreEqual(1, snapshot.FailedWrites);
        Assert.IsFalse(snapshot.IsWithinSlo);
    }

    [TestMethod]
    public void Logger_Metricas_ContabilizanEscriturasCorrectas()
    {
        using var logger = new BlsLogger(_logDir);
        logger.Info("X", "uno");
        logger.Warn("X", "dos");

        var snapshot = logger.Metrics.Snapshot();
        Assert.AreEqual(2, snapshot.TotalWrites);
        Assert.AreEqual(0, snapshot.FailedWrites);
        Assert.AreEqual(1.0, snapshot.WriteSuccessRate, 0.0001);
        // IsWithinSlo incluye p95 <= 50 ms: dependeria de la carga de la
        // maquina y haria el test no determinista (skill 09). El SLO se
        // verifica con reloj inyectable en BlsSloMetricsTests.
    }
}
