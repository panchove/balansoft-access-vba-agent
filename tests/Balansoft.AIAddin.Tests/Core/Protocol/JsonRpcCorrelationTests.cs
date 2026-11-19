// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Standard: OpenTelemetry / ISO/IEC 27001 A.8.15 / ISO/IEC/IEEE 29119
// Architecture Reference: ADR-016-AUDIT-LOGGING.md

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol;

/// <summary>
/// F3 / R-05: el correlation_id y el trace_id deben sobrevivir al viaje por
/// el pipe y aparecer en los logs del dispatcher con los mismos valores.
/// </summary>
[TestClass]
public sealed class JsonRpcCorrelationTests
{
    private static JsonRpcRequest Request(string method, string? correlation = null, string? trace = null)
    {
        return new JsonRpcRequest(
            "2.0",
            JsonSerializer.SerializeToElement(1),
            method,
            JsonSerializer.SerializeToElement(new { }),
            correlation,
            trace);
    }

    [TestMethod]
    public void Serializar_ConCorrelacion_IncluyeLosCampos()
    {
        var json = JsonSerializer.Serialize(Request("list_modules", "corr-1", "traza-1"));

        StringAssert.Contains(json, "\"correlation_id\":\"corr-1\"");
        StringAssert.Contains(json, "\"trace_id\":\"traza-1\"");
    }

    [TestMethod]
    public void Serializar_SinCorrelacion_OmiteLosCampos()
    {
        var json = JsonSerializer.Serialize(Request("list_modules"));

        Assert.IsFalse(json.Contains("correlation_id"));
        Assert.IsFalse(json.Contains("trace_id"));
    }

    [TestMethod]
    public void Deserializar_ConCorrelacion_LeeLosCampos()
    {
        const string json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"list_modules\",\"params\":{},\"correlation_id\":\"corr-2\",\"trace_id\":\"traza-2\"}";

        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        Assert.IsNotNull(request);
        Assert.AreEqual("corr-2", request!.CorrelationId);
        Assert.AreEqual("traza-2", request.TraceId);
    }

    [TestMethod]
    public async Task Dispatcher_ConCorrelacion_LosEscribeEnElLog()
    {
        var logger = new FakeLogger();
        var dispatcher = new JsonRpcDispatcher(
            new MockVbe(), new FakeAccess(), new TestUiScheduler(), logger);

        var response = await dispatcher.DispatchAsync(
            Request("list_modules", "corr-dispatcher", "traza-dispatcher"),
            CancellationToken.None);

        Assert.IsNull(response.Error);
        Assert.IsTrue(logger.Entries.Any(e =>
            e.Message.Contains("corr-dispatcher") && e.Message.Contains("traza-dispatcher")),
            "El dispatcher debe registrar la correlación recibida en el request.");
    }
}
