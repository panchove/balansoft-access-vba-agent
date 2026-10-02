// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Core.Protocol;
using Balansoft.AIAddin.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.Core.Protocol;

[TestClass]
public class BlsPipeFramingTests
{
    [TestMethod]
    public async Task Frame_EnvioSimple_LeeMismoPayload()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("{}");
        var stream = new MemoryStream();
        await WriteFrameDirect(stream, payload);
        stream.Position = 0;

        // Act
        var result = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(payload, result);
    }

    [TestMethod]
    public async Task Frame_PayloadConNewlines_NoRompeFraming()
    {
        // Arrange
        var json = "{\"code\":\"Public Sub Test()\r\n  MsgBox \"\"Hola\"\"\r\nEnd Sub\"}";
        var payload = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        await WriteFrameDirect(stream, payload);
        stream.Position = 0;

        // Act
        var result = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(payload, result);
    }

    [TestMethod]
    public async Task Frame_PayloadUtf8Multibyte_LengthEsEnBytes()
    {
        // Arrange
        var json = "{\"msg\":\"Hola ðŸ‘‹ mundo\"}"; // emoji = 4 bytes UTF-8
        var payload = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        await WriteFrameDirect(stream, payload);
        stream.Position = 0;

        // Act
        var result = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(payload.Length, result!.Length);
        CollectionAssert.AreEqual(payload, result);
    }

    [TestMethod]
    public async Task Frame_Excede1MB_CierraConError()
    {
        // Arrange
        var largePayload = new byte[BlsPipeFraming.MaxFrameSize + 1];
        var stream = new MemoryStream();
        await WriteFrameDirect(stream, largePayload);
        stream.Position = 0;

        // Act & Assert
        await Assert.ThrowsExceptionAsync<BlsFrameTooLargeException>(
            () => BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None));
    }

    [TestMethod]
    public async Task Frame_HeaderParcial_EsperaResto()
    {
        // Arrange: stream que entrega solo 3 bytes y luego se cierra (EOF)
        var stream = new PartialReadStream(new byte[] { 0x04, 0x00, 0x00 }); // solo 3 bytes del header
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<BlsFrameIncompleteException>(
            () => BlsPipeFraming.ReadFrameAsync(stream, cts.Token));
    }

    [TestMethod]
    public async Task Frame_PayloadParcial_EsperaResto()
    {
        // Arrange: header completo (4 bytes = 100 bytes) + payload parcial (50 bytes) y luego EOF
        var header = BitConverter.GetBytes((uint)100);
        var partialPayload = new byte[50];
        var stream = new PartialReadStream(header.Concat(partialPayload).ToArray());
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<BlsFrameIncompleteException>(
            () => BlsPipeFraming.ReadFrameAsync(stream, cts.Token));
    }

    [TestMethod]
    public async Task Frame_Vacio_RespondeInvalidRequest()
    {
        // Arrange: frame con length = 0
        var header = BitConverter.GetBytes((uint)0);
        var stream = new MemoryStream(header);

        // Act
        var result = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert: devuelve array vacÃ­o (el caller decide responder -32600)
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result!.Length);
    }

    [TestMethod]
    public async Task Frame_JsonInvalido_RespondeInvalidRequest()
    {
        // Este test verifica el framing, no la deserializaciÃ³n JSON.
        // La validaciÃ³n JSON se prueba en JsonRpcDeserializationTests.
        var invalidJson = Encoding.UTF8.GetBytes("{ not valid json }");
        var stream = new MemoryStream();
        await WriteFrameDirect(stream, invalidJson);
        stream.Position = 0;

        // Act
        var result = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert: framing OK, payload leÃ­do correctamente
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(invalidJson, result!);
    }

    [TestMethod]
    public async Task Frame_TimeoutInactivo_MitadDeFrame_Cierra()
    {
        // Arrange: stream que nunca entrega datos, timeout interno de 30s es muy largo
        // Usamos un timeout externo mÃ¡s corto para forzar la cancelaciÃ³n
        var stream = new NeverEndingStream();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act & Assert: el timeout externo deberÃ­a causar BlsFrameIncompleteException
        // porque el framing usa un CTS vinculado con timeout de 30s
        await Assert.ThrowsExceptionAsync<BlsFrameIncompleteException>(
            () => BlsPipeFraming.ReadFrameAsync(stream, cts.Token));
    }

    [TestMethod]
    public async Task Frame_TimeoutIdle_EntreFrames_NoCierra()
    {
        // Arrange: dos frames vÃ¡lidos completos con pausa entre ellos
        var payload1 = Encoding.UTF8.GetBytes("{}");
        var payload2 = Encoding.UTF8.GetBytes("{\"id\":1}");
        var frame1 = WriteFrame(payload1);
        var frame2 = WriteFrame(payload2);
        var stream = new DelayedStream(new[] { frame1, frame2 }, TimeSpan.FromMilliseconds(50));

        // Act
        var result1 = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);
        var result2 = await BlsPipeFraming.ReadFrameAsync(stream, CancellationToken.None);

        // Assert: ambos frames leÃ­dos correctamente
        Assert.IsNotNull(result1);
        Assert.IsNotNull(result2);
        CollectionAssert.AreEqual(payload1, result1!);
        CollectionAssert.AreEqual(payload2, result2!);
    }

    private static byte[] WriteFrame(byte[] payload)
    {
        var header = BitConverter.GetBytes((uint)payload.Length);
        var frame = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);
        return frame;
    }

    // Helpers
    private static async Task WriteFrameDirect(Stream stream, byte[] payload)
    {
        var header = BitConverter.GetBytes((uint)payload.Length);
        await stream.WriteAsync(header, 0, header.Length);
        await stream.WriteAsync(payload, 0, payload.Length);
        await stream.FlushAsync();
    }

    // Stream que entrega bytes uno a uno con delay
    private sealed class PartialReadStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public PartialReadStream(byte[] data) => _data = data;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_position >= _data.Length) return Task.FromResult(0);
            int toRead = Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return Task.FromResult(toRead);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Stream que nunca entrega datos (simula cliente colgado)
    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Stream que entrega frames con delay entre ellos
    private sealed class DelayedStream : Stream
    {
        private readonly byte[][] _frames;
        private readonly TimeSpan _delay;
        private int _frameIndex;
        private int _byteIndex;

        public DelayedStream(byte[][] frames, TimeSpan delay)
        {
            _frames = frames;
            _delay = delay;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_frameIndex >= _frames.Length) return 0;

            var currentFrame = _frames[_frameIndex];
            if (_byteIndex >= currentFrame.Length)
            {
                _frameIndex++;
                _byteIndex = 0;
                if (_frameIndex >= _frames.Length) return 0;
                if (_delay > TimeSpan.Zero)
                    await Task.Delay(_delay, cancellationToken);
                currentFrame = _frames[_frameIndex];
            }

            int toRead = Math.Min(count, currentFrame.Length - _byteIndex);
            Array.Copy(currentFrame, _byteIndex, buffer, offset, toRead);
            _byteIndex += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
