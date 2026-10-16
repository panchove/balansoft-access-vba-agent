// Copyright © 2026 Balansoft
// SPDX-License-Identifier: LGPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Balansoft.AIAddin.Tests.UI;

[TestClass]
public sealed class NimTerminalIconTests
{
    private static readonly int[] IconSizes = { 256, 128, 64, 48, 32, 16 };

    // Rango central del badge en la imagen fuente de referencia (920x920).
    private static readonly Rectangle BadgeCrop = new Rectangle(88, 88, 848, 848);

    // NOTA: la regeneración del icono distribuido
    // (src/Balansoft.AIAddin.Access/Resources/NimTerminal.ico) es una tarea de
    // herramienta, no de la suite. El test verifica el algoritmo de generación
    // de forma hermética: no lee ficheros externos ni escribe en el árbol fuente.
    private static void BuildIcoFromImage(Image source, string targetIcoPath, int[] sizes)
    {
        using var cropped = CropBadge(source);

        var pngBuffers = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            using var resized = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(cropped, 0, 0, sz, sz);
            }

            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            pngBuffers[i] = ms.ToArray();
        }

        var dir = Path.GetDirectoryName(targetIcoPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(targetIcoPath);
        using var bw = new BinaryWriter(fs);

        // Header: ICONDIR (6 bytes)
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // icon type (1 = icon)
        bw.Write((ushort)sizes.Length);

        // Directory entries: 16 bytes each
        int offset = 6 + (16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            bw.Write((byte)(sz >= 256 ? 0 : sz));
            bw.Write((byte)(sz >= 256 ? 0 : sz));
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // planes
            bw.Write((ushort)32); // bit count
            bw.Write((uint)pngBuffers[i].Length);
            bw.Write((uint)offset);
            offset += pngBuffers[i].Length;
        }

        // Image payloads
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngBuffers[i]);
        }
    }

    private static Bitmap CropBadge(Image source)
    {
        // Recorta el badge central si la imagen es lo bastante grande; si no,
        // usa la imagen completa (permite alimentar bitmaps sintéticos).
        var crop = BadgeCrop.Right <= source.Width && BadgeCrop.Bottom <= source.Height
            ? BadgeCrop
            : new Rectangle(0, 0, source.Width, source.Height);

        var cropped = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        }

        // Vuelve transparentes las esquinas oscuras del badge.
        for (int y = 0; y < cropped.Height; y++)
        {
            for (int x = 0; x < cropped.Width; x++)
            {
                int dx = Math.Min(x, cropped.Width - 1 - x);
                int dy = Math.Min(y, cropped.Height - 1 - y);
                if (dx + dy < 160)
                {
                    var c = cropped.GetPixel(x, y);
                    int brightness = Math.Max(c.R, Math.Max(c.G, c.B));
                    if (brightness < 18)
                    {
                        cropped.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    }
                    else if (brightness < 45)
                    {
                        int alpha = (int)((brightness - 18) / 27.0 * 255.0);
                        if (alpha < 0) alpha = 0;
                        if (alpha > 255) alpha = 255;
                        cropped.SetPixel(x, y, Color.FromArgb(alpha, c.R, c.G, c.B));
                    }
                }
            }
        }

        return cropped;
    }

    [TestMethod]
    public void CanGenerateAndLoadNimTerminalIcon()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "bls-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string icoPath = Path.Combine(tempDir, "NimTerminal.ico");

        try
        {
            // Imagen sintética en memoria: el test no depende de ficheros
            // externos ni escribe en el árbol de fuentes.
            using var source = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.FromArgb(255, 30, 30, 30));
                using var brush = new SolidBrush(Color.FromArgb(255, 0, 200, 120));
                g.FillEllipse(brush, 96, 96, 320, 320);
            }

            BuildIcoFromImage(source, icoPath, IconSizes);

            Assert.IsTrue(File.Exists(icoPath), "El .ico no se generó.");

            var bytes = File.ReadAllBytes(icoPath);
            Assert.IsTrue(bytes.Length > 6 + 16 * IconSizes.Length, "El .ico está vacío o truncado.");
            Assert.AreEqual(0, bytes[0], "ICONDIR.reserved");
            Assert.AreEqual(0, bytes[1], "ICONDIR.reserved");
            Assert.AreEqual(1, bytes[2], "ICONDIR.type");
            Assert.AreEqual(0, bytes[3], "ICONDIR.type");
            Assert.AreEqual(IconSizes.Length, bytes[4] | (bytes[5] << 8), "Nº de entradas ICONDIR");

            using var icon = new Icon(icoPath);
            Assert.IsNotNull(icon);
            Assert.IsTrue(icon.Width > 0);
            Assert.IsTrue(icon.Height > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void NimTerminalIcon_IsEmbeddedAndLoadable()
    {
        using var stream = typeof(Balansoft.AIAddin.UI.BlsNimTerminal).Assembly
            .GetManifestResourceStream("Balansoft.AIAddin.Access.Resources.NimTerminal.ico");
        Assert.IsNotNull(stream, "Recurso incrustado NimTerminal.ico no encontrado en Balansoft.AIAddin.Access.");

        using var icon = new Icon(stream);
        Assert.IsNotNull(icon);
        Assert.IsTrue(icon.Width > 0);
        Assert.IsTrue(icon.Height > 0);
    }
}
