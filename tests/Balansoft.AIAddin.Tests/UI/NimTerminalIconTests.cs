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
    private static void BuildIcoFromImage(string sourceJpgPath, string targetIcoPath, int[] sizes)
    {
        using var src = Image.FromFile(sourceJpgPath);

        // Crop the central badge (104, 100) -> (920, 920) with a small margin
        var cropRect = new Rectangle(88, 88, 848, 848);
        using var cropped = new Bitmap(cropRect.Width, cropRect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, cropRect.Width, cropRect.Height), cropRect, GraphicsUnit.Pixel);
        }

        // Soften / make transparent the outer corners (distance from corners < 160 px and near-black)
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

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Balansoft.AIAddin.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repositorio no encontrado.");
    }

    [TestMethod]
    public void CanGenerateAndLoadNimTerminalIcon()
    {
        string sourceJpg = @"C:\Users\fprad\.gemini\antigravity-ide\brain\04c580c5-e2a4-4bc5-80d1-97aae8a267e4\nim_terminal_icon_1792102309079.jpg";
        if (!File.Exists(sourceJpg))
        {
            Assert.Inconclusive("Source image not found: " + sourceJpg);
            return;
        }

        string repoRoot = FindRepositoryRoot();
        string resourceIco = Path.Combine(repoRoot, "src", "Balansoft.AIAddin.Access", "Resources", "NimTerminal.ico");
        int[] sizes = new[] { 256, 128, 64, 48, 32, 16 };

        BuildIcoFromImage(sourceJpg, resourceIco, sizes);
        Assert.IsTrue(File.Exists(resourceIco));

        using (var icon = new Icon(resourceIco))
        {
            Assert.IsNotNull(icon);
            Assert.IsTrue(icon.Width > 0);
            Assert.IsTrue(icon.Height > 0);
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
