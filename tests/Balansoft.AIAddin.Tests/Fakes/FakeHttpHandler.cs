// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.Tests.Fakes;

/// <summary>
/// PeticiÃ³n registrada por <see cref="FakeHttpHandler"/>.
/// El cuerpo y los headers se copian en el momento del envÃ­o porque
/// <see cref="BlsNimClient"/> descarta (dispone) el request al terminar.
/// </summary>
public sealed class RecordedRequest
{
    public string Method { get; set; }
    public string Uri { get; set; }
    public string Body { get; set; }
    public string Authorization { get; set; }
    public string Accept { get; set; }

    public override string ToString() => $"{Method} {Uri}";
}

/// <summary>
/// HttpMessageHandler fake que devuelve respuestas SSE controladas en orden FIFO.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses =
        new Queue<Func<HttpResponseMessage>>();

    private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();
    private readonly object _gate = new object();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) { return _requests.ToList(); } }
    }

    public int RequestCount
    {
        get { lock (_gate) { return _requests.Count; } }
    }

    /// <summary>
    /// Encola una respuesta SSE construida con las lÃ­neas dadas.
    /// </summary>
    public void EnqueueSseSuccess(params string[] lines)
    {
        _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new SseStreamContent(lines)
        });
    }

    /// <summary>
    /// Encola una respuesta SSE que nunca entrega datos, hasta que se cancele.
    /// </summary>
    public void EnqueueNeverEndingStream()
    {
        _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new NeverEndingStreamContent()
        });
    }

    /// <summary>
    /// Encola una respuesta de error HTTP con body opcional.
    /// </summary>
    public void EnqueueError(HttpStatusCode statusCode, string body = "", TimeSpan? retryAfter = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (retryAfter.HasValue)
                response.Headers.TryAddWithoutValidation("Retry-After", ((int)retryAfter.Value.TotalSeconds).ToString());

            return response;
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var recorded = new RecordedRequest
        {
            Method = request.Method.Method,
            Uri = request.RequestUri?.ToString() ?? string.Empty,
            Body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false),
            Authorization = request.Headers.Authorization?.ToString(),
            Accept = request.Headers.Accept?.ToString()
        };

        lock (_gate) { _requests.Add(recorded); }

        cancellationToken.ThrowIfCancellationRequested();

        Func<HttpResponseMessage> factory;
        lock (_gate)
        {
            factory = _responses.Count > 0
                ? _responses.Dequeue()
                : (Func<HttpResponseMessage>)(() => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new SseStreamContent("data: [DONE]")
                });
        }

        return factory();
    }

    /// <summary>
    /// Content que emite lÃ­neas SSE y cierra el stream al final.
    /// </summary>
    private sealed class SseStreamContent : HttpContent
    {
        private readonly string[] _lines;

        public SseStreamContent(params string[] lines)
        {
            _lines = lines ?? new string[0];
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var payload = new StringBuilder();
            foreach (var line in _lines)
            {
                payload.Append("data: ").Append(line).Append("\n\n");
            }

            var bytes = Encoding.UTF8.GetBytes(payload.ToString());
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    /// <summary>
    /// Content cuyo stream entrega un keep-alive y luego se bloquea hasta que
    /// se cancele la lectura. Permite verificar el timeout por chunk.
    /// </summary>
    private sealed class NeverEndingStreamContent : HttpContent
    {
        public NeverEndingStreamContent()
        {
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            throw new NotSupportedException("Usar CreateContentReadStreamAsync.");
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            var buffer = new MemoryStream();
            var bytes = Encoding.UTF8.GetBytes(": keep-alive\n\n");
            buffer.Write(bytes, 0, bytes.Length);
            buffer.Position = 0;

            return Task.FromResult<Stream>(new BlockingAfterFirstChunkStream(buffer));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    /// <summary>
    /// Stream que entrega el primer chunk y luego bloquea honrando el token,
    /// de forma similar a una conexiÃ³n SSE que deja de enviar datos.
    /// </summary>
    private sealed class BlockingAfterFirstChunkStream : Stream
    {
        private readonly MemoryStream _firstChunk;
        private bool _drained;

        public BlockingAfterFirstChunkStream(MemoryStream firstChunk)
        {
            _firstChunk = firstChunk;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_drained)
            {
                _drained = true;
                return await _firstChunk.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Solo lectura asÃ­ncrona.");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

