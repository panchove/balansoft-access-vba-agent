// Copyright c 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later
// Architecture Reference: BalanceSoft-002 / ADR-007

using System;
using System.IO;
using System.Text;
using System.Threading;
using Balansoft.AIAddin.UI.Nim;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI.Nim
{
    /// <summary>
    /// BalanceSoft-002: el lector SSE no debe retener lineas sin limite.
    /// </summary>
    [TestClass]
    public class SseLineReaderTests
    {
        private static MemoryStream Stream(string text)
            => new MemoryStream(Encoding.UTF8.GetBytes(text));

        [TestMethod]
        public void ReadLineAsync_LineasNormales_LeeSecuencialmente()
        {
            using var stream = Stream("data: uno\r\n\r\ndata: dos\r\n");
            using var reader = new SseLineReader(stream);

            Assert.AreEqual("data: uno", reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(string.Empty, reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual("data: dos", reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsNull(reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestMethod]
        public void ReadLineAsync_LineaSupera2MB_LanzaInvalidData()
        {
            var huge = new string('x', 2 * 1024 * 1024 + 16) + "\r\n";
            using var stream = Stream(huge);
            using var reader = new SseLineReader(stream);

            Assert.ThrowsException<InvalidDataException>(
                () => reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
