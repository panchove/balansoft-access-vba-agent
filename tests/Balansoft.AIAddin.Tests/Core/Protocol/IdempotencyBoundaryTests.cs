// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017 / auditoria 2026-11-22

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol
{
    /// <summary>
    /// Fase 4: la clave de idempotencia se deriva en el servidor. Un cliente
    /// que cambia la clave que envia no puede burlar la deduplicacion.
    /// </summary>
    [TestClass]
    public class IdempotencyBoundaryTests
    {
        private static JsonRpcRequest Request(string key, string correlationId)
        {
            var parameters = JsonSerializer.SerializeToElement(new { name = "Mod1", code = "Sub A()\nEnd Sub" });
            return new JsonRpcRequest(
                "2.0",
                JsonSerializer.SerializeToElement(1L),
                "write_module",
                parameters,
                correlationId,
                "traza-1",
                key);
        }

        [TestMethod]
        public async Task ClaveDelClienteFalsificada_NoEvitaElReplayDelServidor()
        {
            var vbe = new MockVbe();
            vbe.Modules.Add("Mod1");
            vbe.Code["Mod1"] = string.Empty;
            var dispatcher = new JsonRpcDispatcher(vbe, new FakeAccess(), new TestUiScheduler(), new FakeLogger());

            var first = await dispatcher.DispatchAsync(Request("clave-falsa-1", "turno-1"), CancellationToken.None);
            var second = await dispatcher.DispatchAsync(Request("clave-falsa-2", "turno-1"), CancellationToken.None);

            Assert.IsNull(first.Error);
            Assert.IsNull(second.Error);
            Assert.IsTrue(second.IdempotentReplay == true, "El servidor debe deducir la clave por si mismo.");
            Assert.AreEqual(1, vbe.ExecutedMethods.Count, "El handler solo debe ejecutarse una vez.");
        }
    }
}
