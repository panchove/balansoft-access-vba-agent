// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-016-AUDIT-LOGGING.md / auditoria 2026-11-22

using System;
using System.IO;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers
{
    /// <summary>
    /// Fase 2: la cadena de auditoria no se reinicia cada dia y un borrado
    /// de un dia intermedio se detecta al verificar el directorio.
    /// </summary>
    [TestClass]
    public class BlsAuditChainDirectoryTests
    {
        private string _dir = null!;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "bls-audit-chain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static string MakeLine(string prev, string timestamp, string message, out string hash)
        {
            var payload = "{\"schema_version\":1,\"ts\":\"" + timestamp + "\",\"level\":\"INFO\"," +
                          "\"component\":\"X\",\"message\":\"" + message + "\"," +
                          "\"correlation_id\":\"c1\",\"trace_id\":\"t1\",\"prev_hash\":\"" + prev + "\"}";
            hash = BlsAuditChain.ComputeHash(prev, payload);
            return payload.Substring(0, payload.Length - 1) + ",\"hash\":\"" + hash + "\"}";
        }

        [TestMethod]
        public void VerifyDirectory_DosDiasEncadenados_EsValida()
        {
            var day1 = MakeLine(BlsAuditChain.GenesisHash, "2026-01-01T00:00:00.0000000Z", "uno", out var h1);
            var day2 = MakeLine(h1, "2026-01-02T00:00:00.0000000Z", "dos", out var h2);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260101.log"), day1 + Environment.NewLine);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260102.log"), day2 + Environment.NewLine);

            var result = BlsAuditChain.VerifyDirectory(_dir);

            Assert.IsTrue(result.IsValid, result.Reason);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(h2, result.LastHash);
        }

        [TestMethod]
        public void VerifyDirectory_BorradoDeDiaIntermedio_DetectaCorrupcion()
        {
            var day1 = MakeLine(BlsAuditChain.GenesisHash, "2026-01-01T00:00:00.0000000Z", "uno", out var h1);
            var day2 = MakeLine(h1, "2026-01-02T00:00:00.0000000Z", "dos", out _);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260101.log"), day1 + Environment.NewLine);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260102.log"), day2 + Environment.NewLine);

            File.Delete(Path.Combine(_dir, "balansoft-20260101.log"));

            var result = BlsAuditChain.VerifyDirectory(_dir);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, result.FirstInvalidLine);
        }

        [TestMethod]
        public void SeedFor_DiaSinEventos_UsaUltimoHashDelDiaAnterior()
        {
            var day1 = MakeLine(BlsAuditChain.GenesisHash, "2026-01-01T00:00:00.0000000Z", "uno", out var h1);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260101.log"), day1 + Environment.NewLine);

            var seed = BlsAuditChain.SeedFor(_dir, new DateTime(2026, 1, 2));

            Assert.AreEqual(h1, seed);
        }

        [TestMethod]
        public void Logger_VerifyChain_ConEventosRecientes_EsValida()
        {
            using var logger = new BlsLogger(_dir);
            logger.Info("X", "uno");
            logger.Info("X", "dos");

            var result = logger.VerifyChain();

            Assert.IsTrue(result.IsValid, result.Reason);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void VerifyDirectory_LogsLegacyConGenesisDiario_SigueSiendoValida()
        {
            var day1 = MakeLine(BlsAuditChain.GenesisHash, "2026-01-01T00:00:00.0000000Z", "uno", out _);
            var day2 = MakeLine(BlsAuditChain.GenesisHash, "2026-01-02T00:00:00.0000000Z", "dos", out _);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260101.log"), day1 + Environment.NewLine);
            File.WriteAllText(Path.Combine(_dir, "balansoft-20260102.log"), day2 + Environment.NewLine);

            var result = BlsAuditChain.VerifyDirectory(_dir);

            Assert.IsTrue(result.IsValid, result.Reason);
            Assert.AreEqual(2, result.Count);
        }
    }
}
