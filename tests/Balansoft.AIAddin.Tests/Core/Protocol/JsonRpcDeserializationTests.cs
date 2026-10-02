// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Text.Json;
using Balansoft.AIAddin.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol;

[TestClass]
public class JsonRpcDeserializationTests
{
    [TestMethod]
    public void JsonRpcRequest_DeserializaConIdNumerico()
    {
        // Arrange
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"list_modules\",\"params\":{}}";

        // Act
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        // Assert
        Assert.IsNotNull(request);
        Assert.AreEqual("2.0", request!.JsonRpc);
        Assert.AreEqual(1L, request.GetIdAsObject());
        Assert.AreEqual("list_modules", request.Method);
        Assert.IsTrue(request.Params.HasValue);
    }

    [TestMethod]
    public void JsonRpcRequest_DeserializaConIdString()
    {
        // Arrange
        var json = "{\"jsonrpc\":\"2.0\",\"id\":\"abc-123\",\"method\":\"read_module\",\"params\":{\"name\":\"Utils\"}}";

        // Act
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        // Assert
        Assert.IsNotNull(request);
        Assert.AreEqual("abc-123", request!.GetIdAsObject());
        Assert.AreEqual("read_module", request.Method);
        Assert.IsTrue(request.Params.HasValue);
    }

    [TestMethod]
    public void JsonRpcRequest_ParamsNull_DeserializaComoNull()
    {
        // Arrange
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"list_modules\",\"params\":null}";

        // Act
        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        // Assert
        Assert.IsNotNull(request);
        Assert.IsFalse(request!.Params.HasValue);
    }

    [TestMethod]
    public void JsonRpcResponse_SerializaOk()
    {
        // Arrange
        var response = JsonRpcResponse.Ok(1, new { modules = new[] { "Mod1", "Mod2" } });

        // Act
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        // Assert
        Assert.AreEqual("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("id").GetInt64());
        Assert.IsTrue(doc.RootElement.TryGetProperty("result", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("error", out _));
    }

    [TestMethod]
    public void JsonRpcResponse_SerializaError()
    {
        // Arrange
        var error = new JsonRpcError(
            -32601,
            "Method not found: unknown_method",
            new JsonRpcErrorData("MethodNotFound", "unknown_method", null, null));
        var response = JsonRpcResponse.CreateError("abc", error);

        // Act
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        // Assert
        Assert.AreEqual("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.AreEqual("abc", doc.RootElement.GetProperty("id").GetString());
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var errorElem));
        Assert.AreEqual(-32601, errorElem.GetProperty("code").GetInt32());
        Assert.AreEqual("MethodNotFound", errorElem.GetProperty("data").GetProperty("category").GetString());
    }

    [TestMethod]
    public void JsonRpcResponse_SerializaCancelled()
    {
        // Arrange
        var response = JsonRpcResponse.Cancelled(1);

        // Act
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        // Assert
        Assert.AreEqual("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("id").GetInt64());
        Assert.IsTrue(doc.RootElement.GetProperty("result").TryGetProperty("cancelled", out var cancelled));
        Assert.IsTrue(cancelled.GetBoolean());
        Assert.IsFalse(doc.RootElement.TryGetProperty("error", out _));
    }

    [TestMethod]
    public void JsonRpcErrorData_SoloStackEnDebug()
    {
        // Arrange
        var data = new JsonRpcErrorData("VbeException", "write_module", "0x800A9C68", "stack trace");

        // Act
        var json = JsonSerializer.Serialize(data);

        // Assert
#if DEBUG
        Assert.IsTrue(json.Contains("\"stack\":\"stack trace\""));
#else
        Assert.IsFalse(json.Contains("stack"));
#endif
    }
}
