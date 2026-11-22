// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: IETF AAT / ISO/IEC 27001 A.8.15 / ISO/IEC/IEEE 29119
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.IO;
using System.Text;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

[TestClass]
public sealed class BlsAuditChainTests
{
    private string _logDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "bls-chain-tests-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true);
    }

    private string LogPath => Path.Combine(
        _logDir,
        $"balansoft-{DateTime.UtcNow:yyyyMMdd}.log");

    private void WriteEvent(string payload)
    {
        Directory.CreateDirectory(_logDir);
        var hash = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, payload);
        var line = payload.Substring(0, payload.Length - 1) + ",\"hash\":\"" + hash + "\"}" + Environment.NewLine;
        File.AppendAllText(LogPath, line, Encoding.UTF8);
    }

    [TestMethod]
    public void ComputeHash_MismoPayloadYHashAnterior_EsDeterminista()
    {
        var first = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, "{\"a\":1}");
        var second = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, "{\"a\":1}");

        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first.Length);
    }

    [TestMethod]
    public void ComputeHash_CambiaElPayload_CambiaElHash()
    {
        var first = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, "{\"a\":1}");
        var second = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, "{\"a\":2}");

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void ComputeHash_CambiaElPrevHash_CambiaElHash()
    {
        var first = BlsAuditChain.ComputeHash("A", "{\"a\":1}");
        var second = BlsAuditChain.ComputeHash("B", "{\"a\":1}");

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void TryReadLastHash_SinFichero_DevuelveGenesis()
    {
        var ok = BlsAuditChain.TryReadLastHash(LogPath, out var hash);

        Assert.IsTrue(ok);
        Assert.IsNull(hash);
    }

    [TestMethod]
    public void TryReadLastHash_CadenaValida_DevuelveUltimoHash()
    {
        WriteEvent("{\"ts\":\"2026-11-19T00:00:00Z\",\"prev_hash\":\"GENESIS\"}");
        var esperado = BlsAuditChain.ComputeHash(
            BlsAuditChain.GenesisHash,
            "{\"ts\":\"2026-11-19T00:00:00Z\",\"prev_hash\":\"GENESIS\"}");

        var ok = BlsAuditChain.TryReadLastHash(LogPath, out var hash);

        Assert.IsTrue(ok);
        Assert.AreEqual(esperado, hash);
    }

    [TestMethod]
    public void TryReadLastHash_LineaSinHash_DevuelveFalse()
    {
        Directory.CreateDirectory(_logDir);
        File.WriteAllText(LogPath, "{\"ts\":\"2026-11-19T00:00:00Z\"}" + Environment.NewLine);

        var ok = BlsAuditChain.TryReadLastHash(LogPath, out var hash);

        Assert.IsFalse(ok);
        Assert.IsNull(hash);
    }

    [TestMethod]
    public void Verify_FicheroInexistente_ValidoConCeroEventos()
    {
        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Verify_CadenaValida_CuentaEventos()
    {
        WriteEvent("{\"ts\":\"t1\",\"prev_hash\":\"GENESIS\"}");
        WriteEvent("{\"ts\":\"t2\",\"prev_hash\":\"UTILIZAR\"}");

        // La segunda línea se construye con GENESIS a propósito: se reescribe
        // con el hash real para que la cadena sea válida.
        var lines = File.ReadAllLines(LogPath);
        var prev = BlsAuditChain.ComputeHash(BlsAuditChain.GenesisHash, lines[0].Substring(0, lines[0].LastIndexOf(",\"hash\":\"", StringComparison.Ordinal)) + "}");
        lines[1] = lines[1].Replace("\"prev_hash\":\"UTILIZAR\"", $"\"prev_hash\":\"{prev}\"");
        var payload = lines[1].Substring(0, lines[1].LastIndexOf(",\"hash\":\"", StringComparison.Ordinal)) + "}";
        lines[1] = payload.Substring(0, payload.Length - 1) + ",\"hash\":\"" + BlsAuditChain.ComputeHash(prev, payload) + "\"}";
        File.WriteAllLines(LogPath, lines);

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsTrue(result.IsValid, result.Reason);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Verify_TamperMensaje_DetectaCorrupcion()
    {
        using (var logger = new BlsLogger(_logDir)) logger.Info("A", "mensaje original");
        File.WriteAllText(LogPath, File.ReadAllText(LogPath).Replace("mensaje original", "mensaje cambiado"));

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(1, result.FirstInvalidLine);
        StringAssert.Contains(result.Reason, "Hash");
    }

    [TestMethod]
    public void Verify_TamperPrevHash_DetectaCorrupcion()
    {
        using (var logger = new BlsLogger(_logDir))
        {
            logger.Info("A", "uno");
            logger.Info("B", "dos");
        }
        var lines = File.ReadAllLines(LogPath);
        lines[1] = lines[1].Replace("\"prev_hash\":\"", "\"prev_hash\":\"X");
        File.WriteAllLines(LogPath, lines);

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(2, result.FirstInvalidLine);
        StringAssert.Contains(result.Reason, "prev_hash");
    }

    [TestMethod]
    public void Verify_LineaSinHash_DetectaCorrupcion()
    {
        Directory.CreateDirectory(_logDir);
        File.WriteAllText(LogPath, "{\"ts\":\"2026-11-19T00:00:00Z\"}" + Environment.NewLine);

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(1, result.FirstInvalidLine);
        StringAssert.Contains(result.Reason, "hash final");
    }

    [TestMethod]
    public void Verify_FicheroVacio_ValidoConCeroEventos()
    {
        Directory.CreateDirectory(_logDir);
        File.WriteAllText(LogPath, string.Empty);

        var result = BlsAuditChain.Verify(LogPath);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Count);
    }
}
