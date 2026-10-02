// Copyright Â© 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Balansoft.AIAddin.UI.Nim;

/// <summary>
/// Lector de lÃ­neas SSE con cancelaciÃ³n real.
/// <see cref="StreamReader.ReadLineAsync()"/> no acepta un token en .NET Framework,
/// por lo que un timeout por chunk basado en CancellationToken no serÃ­a observable.
/// Esta clase lee del <see cref="Stream"/> subyacente con
/// <see cref="Stream.ReadAsync(byte[],int,int,CancellationToken)"/>, que sÃ­ honra el token.
/// </summary>
internal sealed class SseLineReader
{
    private const int BufferSize = 4096;

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[BufferSize];
    private readonly MemoryStream _lineBuffer = new MemoryStream();

    private int _bufferPos;
    private int _bufferLen;

    public SseLineReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Lee la siguiente lÃ­nea sin el terminador. Devuelve null en EOF.
    /// </summary>
    public async Task<string> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            while (_bufferPos < _bufferLen)
            {
                var b = _buffer[_bufferPos++];
                if (b == (byte)'\n')
                    return TakeLine();
                _lineBuffer.WriteByte(b);
            }

            _bufferPos = 0;
            _bufferLen = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);

            if (_bufferLen == 0)
            {
                if (_lineBuffer.Length == 0)
                    return null;
                return TakeLine();
            }
        }
    }

    private string TakeLine()
    {
        var bytes = _lineBuffer.ToArray();
        _lineBuffer.SetLength(0);

        if (bytes.Length > 0 && bytes[bytes.Length - 1] == (byte)'\r')
            Array.Resize(ref bytes, bytes.Length - 1);

        return Encoding.UTF8.GetString(bytes);
    }
}

