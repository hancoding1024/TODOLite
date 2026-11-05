using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 生成 app.ico：暖白纸面圆角底 + 深色勾选框（大尺寸额外带两条列表横线），
/// 与界面同色系（#FCFBFA / #3F3C39 / #C9C4BF / #E5E1DC）。
/// 用法：csc /target:exe /out:IconGen.exe tools\IconGen.cs  然后  IconGen.exe app.ico
/// </summary>
internal static class IconGen
{
    private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static readonly Color Tile = Color.FromArgb(252, 251, 250);
    private static readonly Color TileBorder = Color.FromArgb(229, 225, 220);
    private static readonly Color Ink = Color.FromArgb(63, 60, 57);
    private static readonly Color InkFaint = Color.FromArgb(201, 196, 191);

    private static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";

        List<byte[]> images = new List<byte[]>();
        for (int i = 0; i < Sizes.Length; i++)
        {
            images.Add(Render(Sizes[i]));
        }

        using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)Sizes.Length);

            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                int s = Sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)images[i].Length);
                w.Write((uint)offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < images.Count; i++)
            {
                w.Write(images[i]);
            }
        }

        Console.WriteLine("icon written: " + outPath + " (" + Sizes.Length + " sizes)");
        return 0;
    }

    // ------------------------------------------------------------------
    // 绘制
    // ------------------------------------------------------------------
    private static void DrawIcon(Graphics g, int size)
    {
        float f = size;
        float borderWidth = Math.Max(1f, f * 0.02f);

        using (GraphicsPath tile = RoundRect(borderWidth / 2f, borderWidth / 2f, f - borderWidth, f - borderWidth, f * 0.20f))
        {
            using (SolidBrush brush = new SolidBrush(Tile))
                g.FillPath(brush, tile);
            if (size >= 24)
            {
                using (Pen pen = new Pen(TileBorder, borderWidth))
                    g.DrawPath(pen, tile);
            }
        }

        if (size >= 48) DrawListGlyph(g, f);
        else DrawCheckGlyph(g, f);
    }

    /// <summary>小尺寸：整块勾选框 + 白色对勾，保证 16px 也看得清。</summary>
    private static void DrawCheckGlyph(Graphics g, float f)
    {
        using (GraphicsPath box = RoundRect(f * 0.14f, f * 0.14f, f * 0.72f, f * 0.72f, f * 0.20f))
        using (SolidBrush brush = new SolidBrush(Ink))
        {
            g.FillPath(brush, box);
        }

        using (Pen pen = new Pen(Color.White, Math.Max(1.4f, f * 0.13f)))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new PointF[]
            {
                new PointF(f * 0.29f, f * 0.51f),
                new PointF(f * 0.44f, f * 0.66f),
                new PointF(f * 0.72f, f * 0.35f)
            });
        }
    }

    /// <summary>大尺寸：勾选框 + 两条列表横线，明确表达"待办清单"。</summary>
    private static void DrawListGlyph(Graphics g, float f)
    {
        using (GraphicsPath box = RoundRect(f * 0.13f, f * 0.27f, f * 0.29f, f * 0.29f, f * 0.075f))
        using (SolidBrush brush = new SolidBrush(Ink))
        {
            g.FillPath(brush, box);
        }

        using (Pen pen = new Pen(Color.White, Math.Max(1.5f, f * 0.05f)))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new PointF[]
            {
                new PointF(f * 0.19f, f * 0.415f),
                new PointF(f * 0.245f, f * 0.475f),
                new PointF(f * 0.365f, f * 0.355f)
            });
        }

        using (GraphicsPath bar1 = RoundRect(f * 0.50f, f * 0.3775f, f * 0.37f, f * 0.075f, f * 0.0375f))
        using (SolidBrush brush = new SolidBrush(Ink))
        {
            g.FillPath(brush, bar1);
        }

        using (GraphicsPath bar2 = RoundRect(f * 0.50f, f * 0.5475f, f * 0.26f, f * 0.075f, f * 0.0375f))
        using (SolidBrush brush = new SolidBrush(InkFaint))
        {
            g.FillPath(brush, bar2);
        }
    }

    private static GraphicsPath RoundRect(float x, float y, float w, float h, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        if (d > w) d = w;
        if (d > h) d = h;
        if (d <= 0f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ------------------------------------------------------------------
    // 输出为 32bpp DIB 形式（兼容性最好，各尺寸都能被系统正确读取）
    // ------------------------------------------------------------------
    private static byte[] Render(int size)
    {
        Color[] pixels = new Color[size * size];

        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                DrawIcon(g, size);
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = bmp.GetPixel(x, y);
                }
            }
        }

        int rowBytes = size * 4;
        int maskRowBytes = ((size + 31) / 32) * 4;
        int imageBytes = rowBytes * size + maskRowBytes * size;
        byte[] data = new byte[40 + imageBytes];

        WriteInt32(data, 0, 40);
        WriteInt32(data, 4, size);
        WriteInt32(data, 8, size * 2);
        WriteInt16(data, 12, 1);
        WriteInt16(data, 14, 32);
        WriteInt32(data, 16, 0);
        WriteInt32(data, 20, imageBytes);
        WriteInt32(data, 24, 0);
        WriteInt32(data, 28, 0);
        WriteInt32(data, 32, 0);
        WriteInt32(data, 36, 0);

        for (int y = 0; y < size; y++)
        {
            int srcRow = size - 1 - y;
            int rowStart = 40 + y * rowBytes;
            for (int x = 0; x < size; x++)
            {
                Color c = pixels[srcRow * size + x];
                int o = rowStart + x * 4;
                data[o] = c.B;
                data[o + 1] = c.G;
                data[o + 2] = c.R;
                data[o + 3] = c.A;
            }
        }

        int maskBase = 40 + rowBytes * size;
        for (int y = 0; y < size; y++)
        {
            int srcRow = size - 1 - y;
            for (int x = 0; x < size; x++)
            {
                if (pixels[srcRow * size + x].A < 128)
                {
                    int o = maskBase + y * maskRowBytes + (x / 8);
                    data[o] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return data;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
