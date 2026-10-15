// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using Balansoft.AIAddin.Helpers;
using Balansoft.AIAddin.UI;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim;

[TestClass]
public class NimStreamParserTests
{
    private RecordingSink _sink;
    private NimStreamParser _parser;

    [TestInitialize]
    public void Setup()
    {
        _sink = new RecordingSink();
        _parser = new NimStreamParser(_sink);
    }

    [TestMethod]
    public void Feed_ChunkUnicoConTexto_EmiteOnTextChunk()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}");

        CollectionAssert.AreEqual(new[] { "Hola" }, _sink.TextChunks);
        Assert.AreEqual(0, _sink.ToolCalls.Count);
    }

    [TestMethod]
    public void Feed_VariosChunks_EmiteTextoEnOrden()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}");
        _parser.Feed("{\"choices\":[{\"delta\":{\"content\":\" mundo\"}}]}");

        CollectionAssert.AreEqual(new[] { "Hola", " mundo" }, _sink.TextChunks);
    }

    [TestMethod]
    public void Feed_ContentVacio_NoEmite()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"content\":\"\"}}]}");

        Assert.AreEqual(0, _sink.TextChunks.Count);
    }

    [TestMethod]
    public void Feed_LineaVaciaYDone_NoEmitenNada()
    {
        _parser.Feed(string.Empty);
        _parser.Feed("   ");
        _parser.Feed("[DONE]");

        Assert.AreEqual(0, _sink.TextChunks.Count);
        Assert.AreEqual(0, _sink.ToolCalls.Count);
    }

    [TestMethod]
    public void Feed_SinChoices_NoEmite()
    {
        _parser.Feed("{\"id\":\"abc\",\"object\":\"chat.completion.chunk\"}");

        Assert.AreEqual(0, _sink.TextChunks.Count);
        Assert.AreEqual(0, _sink.ToolCalls.Count);
    }

    [TestMethod]
    public void Feed_ToolCallUnChunk_EnsamblaCorrectamente()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{\\\"name\\\":\\\"Test\\\"}\"}}]}}]}");

        _parser.Complete();

        Assert.AreEqual(1, _sink.ToolCalls.Count);
        Assert.AreEqual("call_1", _sink.ToolCalls[0].Id);
        Assert.AreEqual("write_module", _sink.ToolCalls[0].Name);
        Assert.AreEqual("{\"name\":\"Test\"}", _sink.ToolCalls[0].ArgumentsJson);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public void Feed_ToolCallFragmentado_EnsamblaCorrectamente()
    {
        // Los argumentos llegan partidos: "{"name":"Test"" y luego "}".
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{\\\"name\\\":\\\"Test\\\"\"}}]}}]}");
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"}\"}}]}}]}");

        _parser.Complete();

        Assert.AreEqual(1, _sink.ToolCalls.Count);
        Assert.AreEqual("call_1", _sink.ToolCalls[0].Id);
        Assert.AreEqual("write_module", _sink.ToolCalls[0].Name);
        Assert.AreEqual("{\"name\":\"Test\"}", _sink.ToolCalls[0].ArgumentsJson);
    }

    [TestMethod]
    public void Feed_MultiplesToolCalls_EmiteEnOrdenDeIndex()
    {
        // Se emiten index 1 antes que index 0 a propósito para verificar el orden.
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"read_module\",\"arguments\":\"{}\"}},{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{}\"}}]}}]}");

        _parser.Complete();

        Assert.AreEqual(2, _sink.ToolCalls.Count);
        Assert.AreEqual("call_1", _sink.ToolCalls[0].Id);
        Assert.AreEqual("write_module", _sink.ToolCalls[0].Name);
        Assert.AreEqual("call_2", _sink.ToolCalls[1].Id);
        Assert.AreEqual("read_module", _sink.ToolCalls[1].Name);
    }

    [TestMethod]
    public void Complete_ToolCallSinIdNiName_NoEmite()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{}\"}}]}}]}");

        _parser.Complete();

        Assert.AreEqual(0, _sink.ToolCalls.Count);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public void Complete_ToolCallConArgumentosInvalidos_EmiteOnErrorYNoToolCall()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{no-json\"}}]}}]}");

        _parser.Complete();

        Assert.AreEqual(0, _sink.ToolCalls.Count);
        Assert.AreEqual(1, _sink.Errors.Count);
        Assert.IsInstanceOfType(_sink.Errors[0], typeof(BlsNimProtocolException));
    }

    [TestMethod]
    public void Complete_LlamadoDosVeces_NoEmiteDuplicados()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"write_module\",\"arguments\":\"{}\"}}]}}]}");

        _parser.Complete();
        _parser.Complete();

        Assert.AreEqual(1, _sink.ToolCalls.Count);
    }

    [TestMethod]
    public void Complete_SinToolCalls_NoEmiteNada()
    {
        _parser.Feed("{\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}");

        _parser.Complete();

        Assert.AreEqual(0, _sink.ToolCalls.Count);
        Assert.AreEqual(0, _sink.Errors.Count);
    }

    [TestMethod]
    public void Feed_JsonInvalido_LanzaBlsNimProtocolException()
    {
        var ex = Assert.ThrowsException<BlsNimProtocolException>(() => _parser.Feed("not valid json"));

        Assert.IsNotNull(ex.InnerException);
        Assert.IsInstanceOfType(ex.InnerException, typeof(System.Text.Json.JsonException));
    }

    [TestMethod]
    public void Feed_KeepAliveYComentario_NoEmiteNada()
    {
        _parser.Feed("{\"choices\":[]}");

        Assert.AreEqual(0, _sink.TextChunks.Count);
        Assert.AreEqual(0, _sink.ToolCalls.Count);
    }

    private sealed class RecordingSink : IBlsNimSink
    {
        public List<string> TextChunks { get; } = new List<string>();
        public List<ToolCallComplete> ToolCalls { get; } = new List<ToolCallComplete>();
        public List<BlsNimException> Errors { get; } = new List<BlsNimException>();
        public int DoneCount { get; private set; }
        public int CancelledCount { get; private set; }

        public void OnTextChunk(string text) => TextChunks.Add(text);
        public void OnToolCall(ToolCallComplete call) => ToolCalls.Add(call);
        public void OnError(BlsNimException ex) => Errors.Add(ex);
        public void OnDone() => DoneCount++;
        public void OnCancelled() => CancelledCount++;
    }
}

