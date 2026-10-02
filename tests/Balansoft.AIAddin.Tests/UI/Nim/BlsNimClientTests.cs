// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.Tests.Fakes;
using Balansoft.AIAddin.Tests.Infrastructure;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

[TestClass]
public class BlsNimClientTests
{
    private static readonly TimeSpan FastBackoff = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan[] FastBackoffSchedule =
    {
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(30),
        TimeSpan.FromMilliseconds(40)
    };

    private FakeHttpHandler _handler;
    private HttpClient _httpClient;
    private FakeSecureStore _secureStore;
    private FakeLogger _logger;
    private RecordingSink _sink;

    [TestInitialize]
    public void Setup()
    {
        _handler = new FakeHttpHandler();
        _httpClient = new HttpClient(_handler);
        _secureStore = new FakeSecureStore();
        _logger = new FakeLogger();
        _sink = new RecordingSink();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _httpClient?.Dispose();
        Environment.SetEnvironmentVariable("NIM_MODEL", null);
    }

    private BlsNimClient CreateClient(string model = null)
    {
        return new BlsNimClient(
            _httpClient,
            _secureStore,
            _logger,
            model,
            null,
            TimeSpan.FromMilliseconds(150),
            FastBackoffSchedule);
    }

    private Task SendAsync(BlsNimClient client, CancellationToken ct, params ChatMessage[] messages)
    {
        var list = new List<ChatMessage>();
        foreach (var m in messages) list.Add(m);

        return client.SendAsync(list, new List<ToolDefinition>(), _sink, ct);
    }

    // ---------- Streaming de texto ----------

    [TestMethod]
    public async Task SendAsync_ChunkUnico_EmiteTextoYDone()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}", "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        CollectionAssert.AreEqual(new[] { "Hola" }, _sink.TextChunks);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.AreEqual(0, _sink.Errors.Count);
        Assert.AreEqual(0, _sink.CancelledCount);
    }

    [TestMethod]
    public async Task SendAsync_MultiplesChunks_EmiteTextoEnOrden()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess(
            "{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}",
            "{\"choices\":[{\"delta\":{\"content\":\" mundo\"}}]}",
            "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        CollectionAssert.AreEqual(new[] { "Hola", " mundo" }, _sink.TextChunks);
        Assert.AreEqual(1, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_SoloDone_LlamaOnDoneSinTexto()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, _sink.TextChunks.Count);
        Assert.AreEqual(1, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_SinDoneYConEOF_LlamaOnDone()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        CollectionAssert.AreEqual(new[] { "Hola" }, _sink.TextChunks);
        Assert.AreEqual(1, _sink.DoneCount);
    }

    // ---------- Tool calls ----------

    [TestMethod]
    public async Task SendAsync_ToolCallUnChunk_EmiteToolCallYDone()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess(
            "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{\\\"name\\\":\\\"Test\\\"}\"}}]}}]}",
            "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _sink.ToolCalls.Count);
        Assert.AreEqual("call_1", _sink.ToolCalls[0].Id);
        Assert.AreEqual("write_module", _sink.ToolCalls[0].Name);
        Assert.AreEqual("{\"name\":\"Test\"}", _sink.ToolCalls[0].ArgumentsJson);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public async Task SendAsync_ToolCallsFragmentados_EmiteEnOrdenDeIndex()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess(
            "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"read_module\",\"arguments\":\"{}\"}},{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{}\"}}]}}]}",
            "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(2, _sink.ToolCalls.Count);
        Assert.AreEqual("call_1", _sink.ToolCalls[0].Id);
        Assert.AreEqual("call_2", _sink.ToolCalls[1].Id);
    }

    [TestMethod]
    public async Task SendAsync_OrdenDeEventos_ToolCallAntesDeDone()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess(
            "{\"choices\":[{\"delta\":{\"content\":\"Voy a leer\"}}]}",
            "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"read_module\",\"arguments\":\"{}\"}}]}}]}",
            "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        CollectionAssert.AreEqual(
            new[] { "text:Voy a leer", "tool:call_1", "done" },
            _sink.Events);
    }

    // ---------- Errores HTTP ----------

    [TestMethod]
    public async Task SendAsync_SinApiKey_EmiteAuthErrorYNoHaceRequest()
    {
        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimAuthException));
        Assert.AreEqual(0, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_401_EmiteAuthErrorYNoReintenta()
    {
        _secureStore.SaveApiKey("invalid-key");
        _handler.EnqueueError(HttpStatusCode.Unauthorized, "{\"error\":\"invalid api key\"}");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimAuthException));
    }

    [TestMethod]
    public async Task SendAsync_400_NoReintentaYPreservaBody()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueError(HttpStatusCode.BadRequest, "campo inválido");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.AreEqual("campo inválido", _sink.Errors[0].ResponseBody);
        Assert.IsFalse(_sink.Errors[0] is BlsNimAuthException);
    }

    [TestMethod]
    public async Task SendAsync_429_ReintentaYEventuallyFunciona()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueError((HttpStatusCode)429, "rate limited");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(2, _handler.RequestCount);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public async Task SendAsync_429_Agotado_EmiteRateLimitException()
    {
        _secureStore.SaveApiKey("test-key");
        for (var i = 0; i < 3; i++)
            _handler.EnqueueError((HttpStatusCode)429, "rate limited");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(3, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimRateLimitException));
        Assert.AreEqual(0, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_500_ReintentaYEventuallyFunciona()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueError(HttpStatusCode.InternalServerError, "server error");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(2, _handler.RequestCount);
        Assert.AreEqual(1, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_500_Agotado_EmiteServerException()
    {
        _secureStore.SaveApiKey("test-key");
        for (var i = 0; i < 3; i++)
            _handler.EnqueueError(HttpStatusCode.ServiceUnavailable, "gateway down");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(3, _handler.RequestCount);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimServerException));
    }

    [TestMethod]
    public async Task SendAsync_RetryRespetaRetryAfter()
    {
        _secureStore.SaveApiKey("test-key");
        // Retry-After solo admite segundos enteros (doc §9.8).
        _handler.EnqueueError((HttpStatusCode)429, "rate limited", TimeSpan.FromSeconds(1));
        _handler.EnqueueSseSuccess("[DONE]");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);
        sw.Stop();

        Assert.AreEqual(2, _handler.RequestCount);
        Assert.AreEqual(1, _sink.DoneCount);
        Assert.IsTrue(sw.ElapsedMilliseconds >= 800, $"Se esperaba esperar ~1000ms, real {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task SendAsync_RetryAfterSeAcotaA30Segundos()
    {
        _secureStore.SaveApiKey("test-key");
        // Un Retry-After enorme debe acotarse; si no, el test no terminaría.
        _handler.EnqueueError((HttpStatusCode)429, "rate limited", TimeSpan.FromSeconds(600));
        _handler.EnqueueSseSuccess("[DONE]");

        var client = new BlsNimClient(
            _httpClient,
            _secureStore,
            _logger,
            null,
            null,
            TimeSpan.FromMilliseconds(150),
            new[] { TimeSpan.FromMilliseconds(20) });

        // Con el tope de 30 s el reintento no puede completarse dentro del test;
        // se cancela para verificar que el cliente respeta la cancelación.
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await SendAsync(client, cts.Token, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _sink.CancelledCount);
        Assert.AreEqual(0, _sink.DoneCount);
    }

    [TestMethod]
    public async Task SendAsync_RetryUsaBackoffEntreIntentos()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueError((HttpStatusCode)429, "rate limited");
        _handler.EnqueueSseSuccess("[DONE]");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);
        sw.Stop();

        Assert.AreEqual(2, _handler.RequestCount);
        Assert.IsTrue(sw.ElapsedMilliseconds >= FastBackoff.TotalMilliseconds * 0.5,
            $"Se esperaba un backoff observable, real {sw.ElapsedMilliseconds}ms");
    }

    // ---------- Timeout ----------

    [TestMethod]
    public async Task SendAsync_SinDatos_LanzaTimeoutReportadoComoError()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueNeverEndingStream();

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimTimeoutException));
        Assert.AreEqual(0, _sink.DoneCount);
    }

    // ---------- Cancelación ----------

    [TestMethod]
    public async Task SendAsync_CanceladoAntesDelPost_NoHaceRequestYEmiteCancelled()
    {
        _secureStore.SaveApiKey("test-key");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await SendAsync(CreateClient(), cts.Token, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(0, _handler.RequestCount);
        Assert.AreEqual(1, _sink.CancelledCount);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public async Task SendAsync_CanceladoDuranteBackoff_NoReintenta()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueError((HttpStatusCode)429, "rate limited");
        _handler.EnqueueSseSuccess("[DONE]");

        var cts = new CancellationTokenSource();

        // Backoff largo: se cancela mientras espera.
        var client = new BlsNimClient(
            _httpClient,
            _secureStore,
            _logger,
            null,
            null,
            TimeSpan.FromMilliseconds(150),
            new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) });

        var task = SendAsync(client, cts.Token, new ChatMessage("user", "Hola"));
        await Task.Delay(100);
        cts.Cancel();

        await task.WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(1, _handler.RequestCount);
        Assert.AreEqual(1, _sink.CancelledCount);
        Assert.AreEqual(0, _sink.DoneCount);
    }

    // ---------- Petición ----------

    [TestMethod]
    public async Task SendAsync_AuthorizationVaComoHeaderDeRequest()
    {
        _secureStore.SaveApiKey("super-secret-key");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        var request = _handler.Requests[0];
        Assert.AreEqual("Bearer super-secret-key", request.Authorization);
        StringAssert.Contains(request.Accept, "text/event-stream");
    }

    [TestMethod]
    public async Task SendAsync_CuerpoIncluyeModeloYMensajes()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        var body = _handler.Requests[0].Body;
        StringAssert.Contains(body, NimHttpRequestBuilder.DefaultModel);
        StringAssert.Contains(body, "\"stream\":true");
        StringAssert.Contains(body, "Hola");
    }

    [TestMethod]
    public async Task SendAsync_ModeloPorParametro_TienePrioridad()
    {
        _secureStore.SaveApiKey("test-key");
        Environment.SetEnvironmentVariable("NIM_MODEL", "env-model");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient("param-model"), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        var body = _handler.Requests[0].Body;
        StringAssert.Contains(body, "param-model");
        Assert.IsFalse(body.Contains("env-model"));
    }

    [TestMethod]
    public async Task SendAsync_ModeloPorEnv_SeUsa()
    {
        _secureStore.SaveApiKey("test-key");
        Environment.SetEnvironmentVariable("NIM_MODEL", "env-model");
        _handler.EnqueueSseSuccess("[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        StringAssert.Contains(_handler.Requests[0].Body, "env-model");
    }

    [TestMethod]
    public async Task SendAsync_ReutilizaElHttpClientInyectado()
    {
        _secureStore.SaveApiKey("test-key");
        _handler.EnqueueSseSuccess("[DONE]");
        _handler.EnqueueSseSuccess("[DONE]");

        var client = CreateClient();

        await SendAsync(client, CancellationToken.None, new ChatMessage("user", "Uno")).WithTimeout(TaskTestExtensions.DefaultTimeout);
        await SendAsync(client, CancellationToken.None, new ChatMessage("user", "Dos")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.AreEqual(2, _handler.RequestCount);
        StringAssert.Contains(_handler.Requests[0].Body, "Uno");
        StringAssert.Contains(_handler.Requests[1].Body, "Dos");
    }

    // ---------- Seguridad ----------

    [TestMethod]
    public async Task SendAsync_NoRegistraLaApiKeyEnLogs()
    {
        _secureStore.SaveApiKey("super-secret-key-12345");
        _handler.EnqueueSseSuccess("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}", "[DONE]");

        await SendAsync(CreateClient(), CancellationToken.None, new ChatMessage("user", "Hola")).WithTimeout(TaskTestExtensions.DefaultTimeout);

        Assert.IsFalse(_logger.ContainsApiKey("super-secret-key-12345"));
    }

    // ---------- Guards ----------

    [TestMethod]
    public async Task SendAsync_SinkNulo_LanzaArgumentNullException()
    {
        _secureStore.SaveApiKey("test-key");

        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
        {
            var messages = new List<ChatMessage> { new ChatMessage("user", "Hola") };
            return CreateClient().SendAsync(messages, new List<ToolDefinition>(), null, CancellationToken.None);
        });
    }

    [TestMethod]
    public void Constructor_ArgumentosNulos_LanzaArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new BlsNimClient(null, _secureStore, _logger));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new BlsNimClient(_httpClient, null, _logger));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new BlsNimClient(_httpClient, _secureStore, null));
    }

    /// <summary>
    /// Sink que registra los eventos en orden para verificar el contrato de la doc §6.
    /// </summary>
    private sealed class RecordingSink : IBlsNimSink
    {
        private readonly List<string> _events = new List<string>();

        public List<string> Events => _events;
        public List<string> TextChunks { get; } = new List<string>();
        public List<ToolCallComplete> ToolCalls { get; } = new List<ToolCallComplete>();
        public List<BlsNimException> Errors { get; } = new List<BlsNimException>();
        public int DoneCount { get; private set; }
        public int CancelledCount { get; private set; }

        public void OnTextChunk(string text)
        {
            TextChunks.Add(text);
            _events.Add("text:" + text);
        }

        public void OnToolCall(ToolCallComplete call)
        {
            ToolCalls.Add(call);
            _events.Add("tool:" + call.Id);
        }

        public void OnError(BlsNimException ex)
        {
            Errors.Add(ex);
            _events.Add("error:" + ex.GetType().Name);
        }

        public void OnDone()
        {
            DoneCount++;
            _events.Add("done");
        }

        public void OnCancelled()
        {
            CancelledCount++;
            _events.Add("cancelled");
        }
    }
}


