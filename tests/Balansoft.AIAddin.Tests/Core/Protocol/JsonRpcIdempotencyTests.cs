// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: ADR-017-IDEMPOTENCY-RESILIENCE.md / specs/018-IDEMPOTENCY-RESILIENCE.md

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol;

/// <summary>
/// F4 / R-06 — Un frame reenviado dentro del mismo turno no debe ejecutar dos
/// veces una escritura en VBE/Access.
/// </summary>
[TestClass]
public class JsonRpcIdempotencyTests
{
    private MockVbe _vbe = null!;
    private FakeAccess _access = null!;
    private TestUiScheduler _ui = null!;
    private FakeLogger _logger = null!;
    private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Setup()
    {
        _vbe = new MockVbe();
        _access = new FakeAccess();
        _ui = new TestUiScheduler();
        _logger = new FakeLogger();
        _vbe.Modules.Add("Utils");
        _vbe.Code["Utils"] = "old code";
    }

    private JsonRpcDispatcher CreateDispatcher(IBlsIdempotencyCache? cache = null)
    {
        return cache == null
            ? new JsonRpcDispatcher(_vbe, _access, _ui, _logger)
            : new JsonRpcDispatcher(_vbe, _access, _ui, _logger, cache);
    }

    private static JsonRpcRequest CreateRequest(
        string method,
        object id,
        object? parameters,
        string? key = null,
        string? correlationId = null)
    {
        var paramsElement = parameters != null
            ? JsonSerializer.SerializeToElement(parameters)
            : (JsonElement?)JsonSerializer.Deserialize<JsonElement>("{}");
        return new JsonRpcRequest(
            "2.0",
            JsonSerializer.SerializeToElement(id),
            method,
            paramsElement,
            correlationId,
            null,
            key);
    }

    private static object WriteParams(string code = "new code") => new { name = "Utils", code };

    private static string KeyFor(object parameters, string correlationId = "turn-1")
    {
        return BlsIdempotencyKey.For(
            "write_module",
            JsonSerializer.SerializeToElement(parameters),
            correlationId);
    }

    private Task<JsonRpcResponse> DispatchAsync(
        JsonRpcDispatcher dispatcher,
        object id,
        object parameters,
        string key,
        string correlationId = "turn-1")
    {
        return dispatcher.DispatchAsync(
            CreateRequest("write_module", id, parameters, key, correlationId),
            CancellationToken.None);
    }

    // ---------- Idempotencia del dispatcher ----------

    [TestMethod]
    public async Task Idempotency_FrameDuplicado_NoEjecutaHandlerDosVeces()
    {
        var dispatcher = CreateDispatcher();
        var parameters = WriteParams();
        var key = KeyFor(parameters);

        await DispatchAsync(dispatcher, 1, parameters, key);
        await DispatchAsync(dispatcher, 2, parameters, key);

        Assert.AreEqual(1, _vbe.ExecutedMethods.Count(m => m == "write_module"),
            "El handler de escritura solo debe ejecutarse una vez para la misma clave.");
    }

    [TestMethod]
    public async Task Idempotency_FrameDuplicado_DevuelveMismoResultadoConIdActual()
    {
        var dispatcher = CreateDispatcher();
        var parameters = WriteParams();
        var key = KeyFor(parameters);

        var first = await DispatchAsync(dispatcher, 1, parameters, key);
        var second = await DispatchAsync(dispatcher, 2, parameters, key);

        Assert.IsNull(second.Error);
        Assert.AreEqual(first.Result, second.Result,
            "El replay debe devolver exactamente el resultado cacheado (write_module no devuelve payload).");
        Assert.AreEqual("new code", _vbe.Code["Utils"],
            "La escritura se aplicó una sola vez con el código del primer frame.");
        Assert.AreEqual(2, second.Id.GetInt32(),
            "La respuesta debe correlacionarse con el id del frame reenviado.");
        Assert.AreEqual(true, second.IdempotentReplay);
        Assert.IsTrue(first.IdempotentReplay != true,
            "La primera ejecución no es un replay.");
    }

    [TestMethod]
    public async Task Idempotency_RespuestaConError_NoSeCachea()
    {
        var dispatcher = CreateDispatcher();
        var parameters = WriteParams();
        var key = KeyFor(parameters);

        _vbe.NextException = new BlsVbeException("fallo transitorio");
        var first = await DispatchAsync(dispatcher, 1, parameters, key);
        Assert.IsNotNull(first.Error);

        _vbe.NextException = null;
        var second = await DispatchAsync(dispatcher, 2, parameters, key);

        Assert.IsNull(second.Error, "El error no debe reutilizarse en un reintento.");
        Assert.AreEqual(2, _vbe.ExecutedMethods.Count(m => m == "write_module"),
            "Una respuesta fallida debe poder ejecutarse de nuevo.");
    }

    [TestMethod]
    public async Task Idempotency_SinClaveDelCliente_ServidorLaDerivaYDeduplica()
    {
        var dispatcher = CreateDispatcher();
        var parameters = WriteParams();

        await dispatcher.DispatchAsync(
            CreateRequest("write_module", 1, parameters), CancellationToken.None);
        await dispatcher.DispatchAsync(
            CreateRequest("write_module", 2, parameters), CancellationToken.None);

        Assert.AreEqual(1, _vbe.ExecutedMethods.Count(m => m == "write_module"),
            "Fase 4: el servidor deriva la clave aunque el cliente no la envie.");
    }

    [TestMethod]
    public async Task Idempotency_CacheExpira_VuelveAEjecutar()
    {
        var cache = new BlsIdempotencyCache(TimeSpan.FromSeconds(10), 256, () => _now);
        var dispatcher = CreateDispatcher(cache);
        var parameters = WriteParams();
        var key = KeyFor(parameters);

        await DispatchAsync(dispatcher, 1, parameters, key);

        _now = _now.AddSeconds(11);
        var second = await DispatchAsync(dispatcher, 2, parameters, key);

        Assert.IsNull(second.Error);
        Assert.AreEqual(2, _vbe.ExecutedMethods.Count(m => m == "write_module"),
            "Después del TTL la clave debe considerarse nueva.");
        Assert.IsTrue(second.IdempotentReplay != true);
    }

    [TestMethod]
    public async Task Idempotency_CacheLleno_EvictaMasAntiguo()
    {
        var cache = new BlsIdempotencyCache(TimeSpan.FromMinutes(5), 2, () => _now);
        var dispatcher = CreateDispatcher(cache);

        var first = WriteParams("code-1");
        var second = WriteParams("code-2");
        var third = WriteParams("code-3");

        await DispatchAsync(dispatcher, 1, first, KeyFor(first));
        await DispatchAsync(dispatcher, 2, second, KeyFor(second));
        await DispatchAsync(dispatcher, 3, third, KeyFor(third));

        Assert.AreEqual(3, _vbe.ExecutedMethods.Count(m => m == "write_module"));

        // Las dos últimas siguen cacheadas: un hit no reordena la caché.
        await DispatchAsync(dispatcher, 4, second, KeyFor(second));
        await DispatchAsync(dispatcher, 5, third, KeyFor(third));
        Assert.AreEqual(3, _vbe.ExecutedMethods.Count(m => m == "write_module"));

        // La primera clave fue evictada (FIFO): vuelve a ejecutarse.
        await DispatchAsync(dispatcher, 6, first, KeyFor(first));
        Assert.AreEqual(4, _vbe.ExecutedMethods.Count(m => m == "write_module"));
    }

    [TestMethod]
    public async Task Idempotency_ClaveLectura_NoSeCachea()
    {
        var dispatcher = CreateDispatcher();
        var request = CreateRequest("list_modules", 1, null, "clave-lectura", "turn-1");

        await dispatcher.DispatchAsync(request, CancellationToken.None);
        await dispatcher.DispatchAsync(CreateRequest("list_modules", 2, null, "clave-lectura", "turn-1"), CancellationToken.None);

        Assert.AreEqual(2, _vbe.ExecutedMethods.Count(m => m == "list_modules"),
            "Las lecturas deben ejecutarse siempre para no devolver estado obsoleto.");
    }

    // ---------- Derivación de la clave ----------

    [TestMethod]
    public void BlsIdempotencyKey_MismoPayload_MismaClave()
    {
        var parameters = WriteParams();
        var element = JsonSerializer.SerializeToElement(parameters);

        var a = BlsIdempotencyKey.For("write_module", element, "turn-1");
        var b = BlsIdempotencyKey.For(
            "write_module",
            JsonSerializer.SerializeToElement(parameters),
            "turn-1");

        Assert.AreEqual(64, a.Length, "La clave es un SHA-256 en hexadecimal.");
        Assert.AreEqual(a, b, "Un reenvío del mismo frame dentro del turno da la misma clave.");
    }

    [TestMethod]
    public void BlsIdempotencyKey_DistintoPayload_DistintaClave()
    {
        var a = BlsIdempotencyKey.For(
            "write_module",
            JsonSerializer.SerializeToElement(WriteParams("code-1")),
            "turn-1");
        var b = BlsIdempotencyKey.For(
            "write_module",
            JsonSerializer.SerializeToElement(WriteParams("code-2")),
            "turn-1");
        var c = BlsIdempotencyKey.For(
            "write_module",
            JsonSerializer.SerializeToElement(WriteParams("code-1")),
            "turn-2");

        Assert.AreNotEqual(a, b, "Distinto código produce distinta clave.");
        Assert.AreNotEqual(a, c, "Distinto turno produce distinta clave (operación legítima).");
    }
}
