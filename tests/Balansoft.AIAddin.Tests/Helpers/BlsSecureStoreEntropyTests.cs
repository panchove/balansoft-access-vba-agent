// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-018-PRIVACY-SUPPLY-CHAIN.md / specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers;

/// <summary>
/// F5 / R-11 — entropía derivada y migración transparente de la API key.
/// Los protectores versionados son deterministas: no dependen de DPAPI real
/// salvo en el test explícito de round-trip.
/// </summary>
[TestClass]
public class BlsSecureStoreEntropyTests
{
    private string _dir = null!;
    private string _path = null!;
    private FakeLogger _logger = null!;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bls-entropy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "api.key");
        _logger = new FakeLogger();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [TestMethod]
    public void MachineEntropy_EstableEntreInstanciasYDistintaDeLaFija()
    {
        var first = BlsMachineEntropy.Current();
        var second = BlsMachineEntropy.Current();
        var legacy = Encoding.UTF8.GetBytes("Balansoft.AIAddin.Access.v1");

        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual(32, first.Length);
        Assert.IsFalse(first.SequenceEqual(legacy),
            "La entropía v2 no puede ser la cadena fija del v1.");
    }

    [TestMethod]
    public void DerivedProtector_ConDpapiReal_RoundTripCorrecto()
    {
        var protector = new DerivedEntropyDpapiDataProtector();
        var store = new BlsSecureStore(protector, _path, _logger);

        store.SaveApiKey("nvapi-testkey-1234567890");

        Assert.IsTrue(store.TryGetApiKey(out var key));
        Assert.AreEqual("nvapi-testkey-1234567890", key);
    }

    [TestMethod]
    public void SaveApiKey_ConProtectorV2_TryGetSinLegacy_DevuelveKey()
    {
        var current = new VersionedProtector(2);
        var store = new BlsSecureStore(current, _path, _logger);

        store.SaveApiKey("key-v2");

        Assert.IsTrue(store.TryGetApiKey(out var key));
        Assert.AreEqual("key-v2", key);
        Assert.AreEqual(2, File.ReadAllBytes(_path)[0],
            "El fichero debe quedar cifrado con la versión 2.");
    }

    [TestMethod]
    public void TryGet_CiphertextLegacy_MigraAProtectorV2()
    {
        var legacy = new VersionedProtector(1);
        var current = new VersionedProtector(2);
        File.WriteAllBytes(_path, legacy.Protect(Encoding.UTF8.GetBytes("legacy-key")));
        var store = new BlsSecureStore(current, _path, _logger, legacy);

        Assert.IsTrue(store.TryGetApiKey(out var key));

        Assert.AreEqual("legacy-key", key);
        Assert.AreEqual(2, File.ReadAllBytes(_path)[0],
            "El fichero se reencripta con la entropía derivada.");
    }

    [TestMethod]
    public void TryGet_TrasMigracion_NoVuelveAUsarLegacy()
    {
        var legacy = new VersionedProtector(1);
        var current = new VersionedProtector(2);
        File.WriteAllBytes(_path, legacy.Protect(Encoding.UTF8.GetBytes("legacy-key")));
        var store = new BlsSecureStore(current, _path, _logger, legacy);

        Assert.IsTrue(store.TryGetApiKey(out _));
        var callsAfterMigration = legacy.UnprotectCalls;

        Assert.IsTrue(store.TryGetApiKey(out var key));
        Assert.AreEqual("legacy-key", key);
        Assert.AreEqual(callsAfterMigration, legacy.UnprotectCalls,
            "Tras la migración el legacy no se vuelve a invocar.");
    }

    [TestMethod]
    public void Migracion_DosVecesConsecutivas_EsIdempotente()
    {
        var legacy = new VersionedProtector(1);
        var current = new VersionedProtector(2);
        File.WriteAllBytes(_path, legacy.Protect(Encoding.UTF8.GetBytes("same-key")));
        var store = new BlsSecureStore(current, _path, _logger, legacy);

        Assert.IsTrue(store.TryGetApiKey(out var first));
        var firstCipher = File.ReadAllBytes(_path);
        Assert.IsTrue(store.TryGetApiKey(out var second));

        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(firstCipher, File.ReadAllBytes(_path));
    }

    [TestMethod]
    public void TryGet_AmbosProtectoresFallan_LanzaBlsSecureStoreException()
    {
        File.WriteAllBytes(_path, new byte[] { 1, 2, 3 });
        var store = new BlsSecureStore(new FailingProtector(), _path, _logger, new FailingProtector());

        var ex = Assert.ThrowsException<BlsSecureStoreException>(() => store.TryGetApiKey(out _));

        StringAssert.Contains(ex.Message, "API key");
        Assert.IsNotNull(ex.InnerException);
    }

    [TestMethod]
    public void SaveApiKey_NuncaEscribeLaKeyEnElLog()
    {
        const string secret = "nvapi-super-secreto-1234567890";
        var store = new BlsSecureStore(new VersionedProtector(2), _path, _logger);

        store.SaveApiKey(secret);
        Assert.IsTrue(store.TryGetApiKey(out _));

        Assert.IsFalse(_logger.ContainsApiKey(secret),
            "La API key jamás aparece en logs (AGENTS.md regla 7).");
    }

    /// <summary>Protector determinista con un byte de versión que valida al leer.</summary>
    private sealed class VersionedProtector : IDataProtector
    {
        private readonly byte _version;

        public VersionedProtector(byte version) => _version = version;

        public int UnprotectCalls { get; private set; }

        public byte[] Protect(byte[] plaintext)
        {
            var result = new byte[plaintext.Length + 1];
            result[0] = _version;
            Buffer.BlockCopy(plaintext, 0, result, 1, plaintext.Length);
            return result;
        }

        public byte[] Unprotect(byte[] ciphertext)
        {
            UnprotectCalls++;
            if (ciphertext == null || ciphertext.Length == 0 || ciphertext[0] != _version)
                throw new CryptographicException("Entropía incorrecta.");

            var result = new byte[ciphertext.Length - 1];
            Buffer.BlockCopy(ciphertext, 1, result, 0, result.Length);
            return result;
        }
    }

    private sealed class FailingProtector : IDataProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("Fallo simulado.");

        public byte[] Unprotect(byte[] ciphertext) => throw new CryptographicException("Fallo simulado.");
    }
}
