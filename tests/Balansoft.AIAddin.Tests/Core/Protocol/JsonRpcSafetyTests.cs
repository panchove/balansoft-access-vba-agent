// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-015-HUMAN-IN-THE-LOOP.md / auditoria 2026-11-22

using System;
using System.Linq;
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
    /// Fase 1: fail-safe y HITL en la frontera del pipe. Un cliente que hable
    /// directamente con el dispatcher no puede ejecutar tools Medium/High sin
    /// el token emitido por la terminal tras la aprobacion humana.
    /// </summary>
    [TestClass]
    public class JsonRpcSafetyTests
    {
        private MockVbe _vbe = null!;
        private FakeAccess _access = null!;
        private TestUiScheduler _ui = null!;
        private FakeLogger _logger = null!;
        private BlsToolAuthorizationRegistry _authorization = null!;
        private BlsSafetyGate _safetyGate = null!;
        private JsonRpcDispatcher _dispatcher = null!;

        private sealed class NoOpIdempotencyCache : IBlsIdempotencyCache
    {
        public bool TryGet(string key, out JsonRpcResponse? response)
        {
            response = null;
            return false;
        }

        public void Store(string key, JsonRpcResponse response) { }
    }
    [TestInitialize]
        public void Setup()
        {
            _vbe = new MockVbe();
            _vbe.Modules.Add("Mod1");
            _vbe.Modules.Add("Mod2");
            _vbe.Code["Mod1"] = "Sub A()\nEnd Sub";
            _vbe.Code["Mod2"] = "Sub B()\nEnd Sub";
            _access = new FakeAccess();
            _ui = new TestUiScheduler();
            _logger = new FakeLogger();
            _authorization = new BlsToolAuthorizationRegistry();
            _safetyGate = new BlsSafetyGate();
            _dispatcher = new JsonRpcDispatcher(
                _vbe, _access, _ui, _logger,
                new BlsIdempotencyCache(), _authorization, _safetyGate);
        }

        private static JsonRpcRequest Request(
            string method,
            object? parameters,
            string? correlationId = "turno-1",
            string? authorizationToken = null)
        {
            var p = parameters == null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(parameters);
            return new JsonRpcRequest(
                "2.0",
                JsonSerializer.SerializeToElement(1L),
                method,
                p,
                correlationId,
                "traza-1",
                null,
                authorizationToken);
        }

        private string Issue(string method, object? parameters)
        {
            var payload = JsonSerializer.Serialize(parameters ?? new { });
            var risk = new BlsToolRiskPolicy().GetRisk(method);
            return _authorization.Issue(
                method,
                null,
                BlsApprovalRequest.Create(method, risk, payload).PayloadSha256);
        }

        [TestMethod]
        public async Task HighRisk_SinToken_DevuelveHitlDeniedYNoEjecuta()
        {
            var response = await _dispatcher.DispatchAsync(
                Request("delete_module", new { name = "Mod1", confirm = true }),
                CancellationToken.None);

            Assert.IsNotNull(response.Error);
            Assert.AreEqual(JsonRpcErrorCodes.HitlDenied, response.Error.Code);
            Assert.AreEqual("HitlDenied", response.Error.Data?.Category);
            Assert.AreEqual(0, _vbe.ExecutedMethods.Count, "El handler no debe ejecutarse.");
            Assert.IsTrue(_logger.Entries.Any(e => e.Message.Contains("denegada en bridge")));
        }

        [TestMethod]
        public async Task HighRisk_ConTokenValido_EjecutaUnaSolaVez()
        {
            var token = Issue("delete_module", new { name = "Mod1", confirm = true });

            var response = await _dispatcher.DispatchAsync(
                Request("delete_module", new { name = "Mod1", confirm = true }, authorizationToken: token),
                CancellationToken.None);

            Assert.IsNull(response.Error, response.Error?.Message);
            CollectionAssert.Contains(_vbe.ExecutedMethods, "delete_module");
        }

        [TestMethod]
        public async Task TokenDeUnSoloUso_SegundoFrameQuedaDenegado()
        {
            var dispatcher = new JsonRpcDispatcher(
                _vbe, _access, _ui, _logger,
                new NoOpIdempotencyCache(), _authorization, _safetyGate);
            var token = Issue("delete_module", new { name = "Mod1", confirm = true });
            var request = Request("delete_module", new { name = "Mod1", confirm = true }, authorizationToken: token);

            var first = await dispatcher.DispatchAsync(request, CancellationToken.None);
            var second = await dispatcher.DispatchAsync(request, CancellationToken.None);

            Assert.IsNull(first.Error);
            Assert.IsNotNull(second.Error);
            Assert.AreEqual(JsonRpcErrorCodes.HitlDenied, second.Error.Code);
            Assert.AreEqual(1, _vbe.ExecutedMethods.Count);
        }

        [TestMethod]
        public async Task KillSwitchDisparado_BloqueaAntesDelHandler()
        {
            _safetyGate.Trip("parada manual");
            var token = Issue("delete_module", new { name = "Mod1", confirm = true });

            var response = await _dispatcher.DispatchAsync(
                Request("delete_module", new { name = "Mod1", confirm = true }, authorizationToken: token),
                CancellationToken.None);

            Assert.IsNotNull(response.Error);
            Assert.AreEqual(JsonRpcErrorCodes.SafetyTripped, response.Error.Code);
            Assert.AreEqual(0, _vbe.ExecutedMethods.Count);
        }

        [TestMethod]
        public async Task LecturaLow_SinToken_SeEjecuta()
        {
            var response = await _dispatcher.DispatchAsync(
                Request("list_modules", null),
                CancellationToken.None);

            Assert.IsNull(response.Error);
            CollectionAssert.Contains(_vbe.ExecutedMethods, "list_modules");
        }

        [TestMethod]
        public async Task LimiteIteracionesPorTurno_SeRefuerzaEnElDispatcher()
        {
            var dispatcher = new JsonRpcDispatcher(
                _vbe, _access, _ui, _logger,
                new BlsIdempotencyCache(), _authorization, _safetyGate, maxToolIterations: 2);

            var a = await dispatcher.DispatchAsync(Request("list_modules", null, "turno-X"), CancellationToken.None);
            var b = await dispatcher.DispatchAsync(Request("list_modules", null, "turno-X"), CancellationToken.None);
            var c = await dispatcher.DispatchAsync(Request("list_modules", null, "turno-X"), CancellationToken.None);

            Assert.IsNull(a.Error);
            Assert.IsNull(b.Error);
            Assert.IsNotNull(c.Error);
            Assert.AreEqual(JsonRpcErrorCodes.ToolIterationLimit, c.Error.Code);
        }

        [TestMethod]
        public async Task FlujoReal_TokenDeTerminal_EjecutaSinHitlDenied()
        {
            var registry = new BlsToolAuthorizationRegistry();
            var localVbe = new MockVbe();
            localVbe.Modules.Add("Mod1");
            localVbe.Code["Mod1"] = string.Empty;
            var dispatcher = new JsonRpcDispatcher(
                localVbe, _access, _ui, _logger,
                new BlsIdempotencyCache(), registry, new BlsSafetyGate());

            using var argsDoc = JsonDocument.Parse("{\"name\":\"Mod1\",\"code\":\"Sub A()\"}");
            var args = argsDoc.RootElement.Clone();
            // exactamente lo que hace BlsBridgeClient y la terminal
            var canonical = JsonSerializer.SerializeToElement(args).GetRawText();
            var risk = new BlsToolRiskPolicy().GetRisk("write_module");
            var token = registry.Issue(
                "write_module", null,
                BlsApprovalRequest.Create("write_module", risk, canonical).PayloadSha256);

            var request = new JsonRpcRequest(
                "2.0", JsonSerializer.SerializeToElement(1L), "write_module",
                JsonSerializer.SerializeToElement(args), null, null, null, token);

            var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

            Assert.IsNull(response.Error, response.Error?.Message);
            CollectionAssert.Contains(localVbe.ExecutedMethods, "write_module");
        }
        [TestMethod]
        public void Registro_TokenCaducado_NoValida()
        {
            var now = DateTimeOffset.UtcNow;
            var current = now;
            var registry = new BlsToolAuthorizationRegistry(
                TimeSpan.FromSeconds(1),
                () => current);

            var token = registry.Issue("delete_module", "turno-1", "hash");
            current = now.AddSeconds(5);
            var valid = registry.Validate(token, "delete_module", "turno-1", "hash");

            Assert.IsFalse(valid, "Un token emitido antes del salto de reloj debe haber caducado.");
        }
    }
}
