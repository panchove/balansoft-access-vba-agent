// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.AccessIntegration;
using Balansoft.AIAddin.Core;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.VbeIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core;

[TestClass]
public class BlsVbeBridgeServerTests
{
    private MockVbe _vbe = null!;
    private FakeAccess _access = null!;
    private TestUiScheduler _ui = null!;
    private FakeLogger _logger = null!;
    private BlsVbeBridgeServer _server = null!;
    private string _testPipeName;

    [TestInitialize]
    public void Setup()
    {
        _vbe = new MockVbe();
        _access = new FakeAccess();
        _ui = new TestUiScheduler();
        _logger = new FakeLogger();
        _testPipeName = $"balansoft-opencode-test-{Guid.NewGuid():N}";
        _server = new BlsVbeBridgeServer(_vbe, _access, _ui, _logger, _testPipeName);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_server.IsRunning)
        {
            await _server.StopAsync(TimeSpan.FromSeconds(5));
        }
        _server.Dispose();
        // PequeÃ±a pausa para permitir que el named pipe se libere completamente
        await Task.Delay(100);
    }

    private async Task<NamedPipeClientStream> ConnectClientAsync()
    {
        var client = new NamedPipeClientStream(".", _testPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        // Reintentar conexiÃ³n hasta que el servidor estÃ© listo
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await client.ConnectAsync(500);
                return client;
            }
            catch (TimeoutException)
            {
                await Task.Delay(50);
            }
        }
        await client.ConnectAsync(1000); // Ãºltimo intento
        return client;
    }

    private static async Task<byte[]> SendReceiveAsync(NamedPipeClientStream client, JsonRpcRequest request)
    {
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request);
        var header = BitConverter.GetBytes((uint)requestBytes.Length);
        await client.WriteAsync(header, 0, header.Length);
        await client.WriteAsync(requestBytes, 0, requestBytes.Length);
        await client.FlushAsync();

        // Leer header
        var respHeader = new byte[4];
        await ReadExactlyAsync(client, respHeader);
        var length = BitConverter.ToUInt32(respHeader, 0);

        // Leer payload
        var payload = new byte[length];
        await ReadExactlyAsync(client, payload);
        return payload;
    }

    private static JsonRpcRequest CreateRequest(string method, object? id = null, object? paramsObj = null)
    {
        var json = paramsObj != null
            ? JsonSerializer.Serialize(paramsObj)
            : "{}";
        var paramsElement = JsonSerializer.Deserialize<JsonElement>(json);
        var idElement = id switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            string s => JsonSerializer.SerializeToElement(s),
            int i => JsonSerializer.SerializeToElement(i),
            _ => JsonSerializer.SerializeToElement(id ?? 1)
        };
        return new JsonRpcRequest("2.0", idElement, method, paramsElement);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
                throw new EndOfStreamException("EOF inesperado");
            totalRead += read;
        }
    }

    private static async Task WaitForStartedAsync(Task startedTask, TimeSpan timeout)
    {
        var completedTask = await Task.WhenAny(startedTask, Task.Delay(timeout));
        if (completedTask != startedTask)
            throw new TimeoutException("El servidor no se iniciÃ³ a tiempo");
        await startedTask; // propagar excepciones
    }

    [TestMethod]
    public async Task Bridge_StartArrancaLoop_NoBloquea()
    {
        // Act
        _server.Start();

        // Assert
        Assert.IsTrue(_server.IsRunning);

        // Cleanup
        await _server.StopAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Bridge_StopAsync_CierraPipeYEspera()
    {
        // Arrange
        _server.Start();

        // Act
        await _server.StopAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsFalse(_server.IsRunning);
    }

    [TestMethod]
    public async Task Bridge_Dispose_Idempotente()
    {
        // Arrange
        _server.Start();
        await _server.StopAsync(TimeSpan.FromSeconds(1));

        // Act & Assert: no debe lanzar
        _server.Dispose();
        _server.Dispose(); // segunda vez
    }

    [TestMethod]
    public async Task Frame_Vacio_RespondeInvalidRequest()
    {
        // Arrange
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));
        var client = await ConnectClientAsync();

        // Act: enviar frame con length=0
        var header = BitConverter.GetBytes((uint)0);
        await client.WriteAsync(header, 0, header.Length);
        await client.FlushAsync();

        // Leer respuesta
        var respHeader = new byte[4];
        await ReadExactlyAsync(client, respHeader);
        var length = BitConverter.ToUInt32(respHeader, 0);
        var payload = new byte[length];
        await ReadExactlyAsync(client, payload);

        // Assert
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(payload);
        Assert.IsNotNull(response!.Error);
        Assert.AreEqual(-32600, response.Error.Code);

        client.Dispose();
    }

    [TestMethod]
    public async Task Frame_JsonInvalido_RespondeInvalidRequest()
    {
        // Arrange
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));
        var client = await ConnectClientAsync();

        // Act: enviar JSON invÃ¡lido
        var invalidJson = Encoding.UTF8.GetBytes("{ not valid }");
        var header = BitConverter.GetBytes((uint)invalidJson.Length);
        await client.WriteAsync(header, 0, header.Length);
        await client.WriteAsync(invalidJson, 0, invalidJson.Length);
        await client.FlushAsync();

        // Leer respuesta
        var respHeader = new byte[4];
        await ReadExactlyAsync(client, respHeader);
        var length = BitConverter.ToUInt32(respHeader, 0);
        var payload = new byte[length];
        await ReadExactlyAsync(client, payload);

        // Assert
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(payload);
        Assert.IsNotNull(response!.Error);
        Assert.AreEqual(-32600, response.Error.Code);

        client.Dispose();
    }

    [TestMethod]
    public async Task Frame_SegundoCliente_RespondeBusy()
    {
        // Arrange: el primer cliente ocupa la Ãºnica conexiÃ³n serviced.
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));

        using var first = await ConnectClientAsync();

        // Act: un segundo cliente intenta conectarse mientras el primero sigue abierto.
        using var second = await ConnectClientAsync();

        var header = new byte[4];
        await ReadExactlyAsync(second, header);
        var length = BitConverter.ToUInt32(header, 0);
        var payload = new byte[length];
        await ReadExactlyAsync(second, payload);

        // Assert
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(payload);
        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Error);
        Assert.AreEqual(JsonRpcErrorCodes.Busy, response.Error.Code);
        StringAssert.Contains(response.Error.Message, "Otro cliente");
    }

    [TestMethod]
    public async Task Turno_CanceladoAntesDeTool_NoEjecutaTool()
    {
        // Arrange: el token del turno ya estÃ¡ cancelado antes de despachar.
        _vbe.Reset();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var dispatcher = new JsonRpcDispatcher(_vbe, _access, _ui, _logger);
        var request = CreateRequest("read_module", 7, new { name = "Modulo1" });

        // Act
        var response = await dispatcher.DispatchAsync(request, cts.Token);

        // Assert: no es un error, es cancelaciÃ³n, y la herramienta no se ejecutÃ³.
        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        Assert.AreEqual(0, _vbe.ExecutedMethods.Count,
            $"La herramienta se ejecutÃ³ pese a la cancelaciÃ³n: {string.Join(",", _vbe.ExecutedMethods)}");
    }

    [TestMethod]
    public async Task Turno_NoCancelado_EjecutaTool()
    {
        // Contrapeso del test anterior: sin cancelaciÃ³n la tool sÃ­ se ejecuta.
        _vbe.Reset();
        _vbe.Code["Modulo1"] = "Sub foo()\nEnd Sub";

        var dispatcher = new JsonRpcDispatcher(_vbe, _access, _ui, _logger);
        var request = CreateRequest("read_module", 7, new { name = "Modulo1" });

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.IsNull(response.Error);
        Assert.IsNotNull(response.Result);
        CollectionAssert.Contains(_vbe.ExecutedMethods, "read_module");
    }

    [TestMethod]
    public async Task Bridge_HandlerFalla_DevuelveErrorNoExito()
    {
        // Arrange
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));
        var client = await ConnectClientAsync();

        // Act: read_module de mÃ³dulo inexistente
        var request = CreateRequest("read_module", 1, new { name = "NoExiste" });
        var responseBytes = await SendReceiveAsync(client, request);

        // Assert
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseBytes);
        Assert.IsNotNull(response!.Error);
        Assert.AreEqual(-32000, response.Error.Code); // ApplicationError
        Assert.IsTrue(response.Error.Message.Contains("VbeException"));
        Assert.IsTrue(response.Error.Message.Contains("no encontrado"));

        client.Dispose();
    }

    [TestMethod]
    public async Task Bridge_CancelacionUsuario_NoEsError()
    {
        // Arrange
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));
        var client = await ConnectClientAsync();

        // Act: request con token cancelado
        var request = CreateRequest("list_modules");
        var responseBytes = await SendReceiveAsync(client, request);

        // Assert: respuesta con result.cancelled, no error
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseBytes);
        Assert.IsNull(response!.Error);
        Assert.IsNotNull(response.Result);

        client.Dispose();
    }

    [TestMethod]
    public async Task Bridge_ClienteReal_ConectaYRecibeRespuesta()
    {
        // Regression: el servidor y el cliente deben coincidir en el nombre del pipe.
        // Se pasa el nombre con prefijo \\.\pipe\ al servidor y el nombre pelado al
        // cliente; ambos deben normalizarse al mismo pipe.
        var bareName = $"balansoft-opencode-test-{Guid.NewGuid():N}";

        using var server = new BlsVbeBridgeServer(
            _vbe, _access, _ui, _logger, @"\\.\pipe\" + bareName);

        server.Start();
        await WaitForStartedAsync(server.StartedAsync, TimeSpan.FromSeconds(5));

        using var client = new BlsBridgeClient(bareName, _logger, TimeSpan.FromSeconds(10));

        var modules = await client.InvokeAsync<JsonElement>(
            "list_modules",
            null,
            CancellationToken.None);

        Assert.AreEqual(JsonValueKind.Array, modules.ValueKind);
    }

    [TestMethod]
    public async Task Bridge_DosInvocacionesConcurrentes_NoIntercalanFrames()
    {
        // El protocolo es request/response: dos llamadas simultÃ¡neas sobre el mismo
        // pipe deben serializarse y devolver cada una su propia respuesta.
        _server.Start();
        await WaitForStartedAsync(_server.StartedAsync, TimeSpan.FromSeconds(5));

        using var client = new BlsBridgeClient(_testPipeName, _logger, TimeSpan.FromSeconds(10));

        var first = client.InvokeAsync<JsonElement>("list_modules", null, CancellationToken.None);
        var second = client.InvokeAsync<JsonElement>("get_active_module", null, CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        foreach (var result in results)
        {
            Assert.AreNotEqual(JsonValueKind.Undefined, result.ValueKind);
        }
    }
}
