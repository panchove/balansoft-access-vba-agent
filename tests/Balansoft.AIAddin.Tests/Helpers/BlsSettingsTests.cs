// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
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
