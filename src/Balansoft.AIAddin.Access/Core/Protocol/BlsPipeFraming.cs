// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Balansoft.AIAddin.Helpers;

namespace Balansoft.AIAddin.Core.Protocol;

/// <summary>
/// Lectura y escritura de frames del Named Pipe.
/// Framing: [uint32 LE length (4 bytes)][UTF-8 JSON payload].
/// Máximo 1 MB por frame (ADR-002).
/// </summary>
public static class BlsPipeFraming
{
    public const int MaxFrameSize = 1024 * 1024; // 1 MB
    private const int HeaderSize = 4;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Nombre del pipe por defecto, sin el prefijo <c>\\.\pipe\</c>: tanto
    /// <see cref="System.IO.Pipes.NamedPipeServerStream"/> como
    /// <see cref="System.IO.Pipes.NamedPipeClientStream"/> lo anteponen por su cuenta.
    /// </summary>
    public const string DefaultPipeName = "balansoft-opencode";

    /// <summary>
    /// Normaliza un nombre de pipe: acepta tanto "balansoft-opencode" como
    /// "\\\\.\\pipe\\balansoft-opencode" y devuelve siempre la forma sin prefijo.
    /// Así servidor y cliente coinciden aunque el nombre venga de configuración.
    /// </summary>
    public static string NormalizePipeName(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            return DefaultPipeName;

        var name = pipeName.Trim();

        const string prefix = @"\\.\pipe\";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(prefix.Length);
        }

        var result = name.Length == 0 ? DefaultPipeName : name;
        return BlsContract.EnsuresNotNull(result, "El nombre de pipe normalizado no puede ser nulo.");
    }

    /// <summary>
    /// Lee un frame completo del stream.
    /// </summary>
    /// <param name="stream">Stream del Named Pipe.</param>
    /// <param name="ct">Token de cancelación del loop del servidor.</param>
    /// <returns>Bytes del payload JSON, o null si EOF limpio.</returns>
    /// <exception cref="BlsFrameTooLargeException">Si length > 1 MB.</exception>
    /// <exception cref="BlsFrameIncompleteException">Si timeout a mitad de frame.</exception>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        BlsContract.RequiresNotNull(stream, nameof(stream));
        BlsContract.Requires(stream.CanRead, "El stream debe permitir lectura para leer un frame.");

        var header = await ReadExactAsync(stream, HeaderSize, ct).ConfigureAwait(false);
        if (header == null)
            return null; // EOF limpio

        uint length = BitConverter.ToUInt32(header, 0);

        if (length == 0)
        {
            // Frame vacío: no es error fatal, el caller decide responder -32600
            return Array.Empty<byte>();
        }

        if (length > MaxFrameSize)
        {
            throw new BlsFrameTooLargeException(length);
        }

        var payload = await ReadExactAsync(stream, (int)length, ct).ConfigureAwait(false);
        if (payload == null)
        {
            throw new BlsFrameIncompleteException("EOF inesperado a mitad de payload");
        }

        return payload;
    }

    /// <summary>
    /// Escribe un frame completo al stream.
    /// </summary>
    /// <param name="stream">Stream del Named Pipe.</param>
    /// <param name="payload">Bytes UTF-8 del JSON serializado.</param>
    /// <param name="ct">Token de cancelación.</param>
    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        BlsContract.RequiresNotNull(stream, nameof(stream));
        BlsContract.Requires(stream.CanWrite, "El stream debe permitir escritura para enviar un frame.");
        BlsContract.RequiresNotNull(payload, nameof(payload));

        var length = payload.Length;
        if (length > MaxFrameSize)
        {
            throw new BlsFrameTooLargeException((uint)length);
        }

        var header = BitConverter.GetBytes((uint)length);
        await stream.WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        int totalRead = 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeout);

        while (totalRead < count)
        {
            try
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    if (totalRead == 0)
                        return null; // EOF limpio al inicio
                    throw new BlsFrameIncompleteException($"EOF inesperado: leídos {totalRead} de {count} bytes");
                }
                totalRead += read;
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new BlsFrameIncompleteException($"Timeout leyendo {count - totalRead} bytes restantes");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelación externa durante la lectura = frame incompleto
                throw new BlsFrameIncompleteException($"Cancelación externa: frame incompleto ({totalRead}/{count} bytes)");
            }
        }

        return buffer;
    }
}
