// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.Net.Http;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Helpers
{
    /// <summary>
    /// Configuracion en caliente (API key + modelo) y version visible.
    /// </summary>
    [TestClass]
    public class BlsSettingsTests
    {
        [TestMethod]
        public void BlsVersion_Current_NoVacioYOpcionalmenteCuatroPartes()
        {
            var version = BlsVersion.Current;

            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
            Assert.IsTrue(Version.TryParse(version.Length == 3 ? version + ".0" : version, out _),
                "La version debe ser parseable: " + version);
        }

        [TestMethod]
        public void BlsSettings_Load_SinFichero_DevuelveDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), "bls-settings-" + Guid.NewGuid().ToString("N") + ".json");

            var settings = BlsSettings.Load(path);

            Assert.AreEqual(BlsSettings.DefaultTemperature, settings.Temperature);
            Assert.AreEqual(BlsSettings.DefaultMaxTokens, settings.MaxTokens);
            Assert.AreEqual(BlsSettings.DefaultMaxToolIterations, settings.MaxToolIterations);
        }

        [TestMethod]
        public void BlsSettings_SaveLoad_RoundTripYClamp()
        {
            var path = Path.Combine(Path.GetTempPath(), "bls-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settings = new BlsSettings
                {
                    Temperature = 9.9,
                    MaxTokens = 1,
                    MaxToolIterations = 9999,
                    Endpoint = "  https://nin.local/v1  ",
                    Language = "en"
                };
                settings.Save(path);

                var loaded = BlsSettings.Load(path);

                Assert.AreEqual(BlsSettings.DefaultTemperature, loaded.Temperature);
                Assert.AreEqual(BlsSettings.DefaultMaxTokens, loaded.MaxTokens);
                Assert.AreEqual(BlsSettings.DefaultMaxToolIterations, loaded.MaxToolIterations);
                Assert.AreEqual("https://nin.local/v1", loaded.Endpoint);
                Assert.AreEqual("en", loaded.Language);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
        [TestMethod]
        public void NimClient_Model_SePuedeCambiarEnCaliente()
        {
            using var client = new BlsNimClient(
                new HttpClient(new FakeHttpHandler()),
                new FakeSecureStore(),
                new FakeLogger());

            client.Model = "  modelo/custom  ";
            Assert.AreEqual("modelo/custom", client.Model);
            Assert.AreEqual("modelo/custom", NimHttpRequestBuilder.ResolveModel(client.Model));

            client.Model = null;
            Assert.AreEqual(BlsModelCatalog.DefaultModel, NimHttpRequestBuilder.ResolveModel(client.Model));
        }
    }
}
