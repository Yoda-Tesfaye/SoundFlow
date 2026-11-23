using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Build-time helper: renders assets/app.ico (a Bluetooth "receive audio" tile) for the
/// executable. Small frames are stored as DIBs, large frames as PNG.
/// </summary>
internal static class IconGen
{
    static readonly int[] Frames = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "app.ico";
        List<int> sizes = new List<int>(Frames);
        List<byte[]> images = new List<byte[]>();

        for (int i = 0; i < sizes.Count; i++)
        {
            using (Bitmap bitmap = Render(sizes[i]))
            {
                images.Add(sizes[i] <= 64 ? ToDib(bitmap) : ToPng(bitmap));
            }
        }

        WriteIcon(output, sizes, images);
        Console.WriteLine("icon written: " + output + " (" + new FileInfo(output).Length + " bytes)");
    }

    static Bitmap Render(int size)
    {
        int supersample = size <= 48 ? 8 : (size <= 64 ? 4 : 2);
        int big = size * supersample;

        Bitmap scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Bitmap large = new Bitmap(big, big, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(large))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                float inset = big * 0.045f;
                RectangleF tile = new RectangleF(inset, inset, big - inset * 2f, big - inset * 2f);
                using (GraphicsPath shape = RoundedRect(tile, tile.Width * 0.235f))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    tile,
                    Color.FromArgb(0x3C, 0x9D, 0xFF),
                    Color.FromArgb(0x0A, 0x5E, 0xC8),
                    90f))
                {
                    graphics.FillPath(brush, shape);
                }

                float centerX = big * 0.5f;
                float top = big * 0.25f;
                float bottom = big * 0.75f;
                float middle = (top + bottom) / 2f;
                float wing = big * 0.135f;
                float shoulderTop = top + (middle - top) * 0.42f;
                float shoulderBottom = bottom - (bottom - middle) * 0.42f;

                using (Pen pen = new Pen(Color.White, big * 0.068f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLine(pen, centerX, top, centerX, bottom);
                    graphics.DrawLine(pen, centerX, top, centerX + wing, shoulderTop);
                    graphics.DrawLine(pen, centerX + wing, shoulderTop, centerX, middle);
                    graphics.DrawLine(pen, centerX, bottom, centerX + wing, shoulderBottom);
                    graphics.DrawLine(pen, centerX + wing, shoulderBottom, centerX, middle);
                }
            }

            using (Graphics graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.DrawImage(large, new Rectangle(0, 0, size, size));
            }
        }
        return scaled;
    }

    static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = Math.Max(1f, radius * 2f);
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    static byte[] ToPng(Bitmap bitmap)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    static byte[] ToDib(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int xorStride = width * 4;
        int andStride = ((width + 31) / 32) * 4;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40);                 // biSize
            writer.Write(width);              // biWidth
            writer.Write(height * 2);         // biHeight (xor + and)
            writer.Write((ushort)1);          // biPlanes
            writer.Write((ushort)32);         // biBitCount
            writer.Write(0);                  // biCompression = BI_RGB
            writer.Write(xorStride * height + andStride * height);
            writer.Write(0);                  // biXPelsPerMeter
            writer.Write(0);                  // biYPelsPerMeter
            writer.Write(0);                  // biClrUsed
            writer.Write(0);                  // biClrImportant

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[xorStride];
                for (int y = height - 1; y >= 0; y--)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, xorStride);
                    writer.Write(row);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            byte[] maskRow = new byte[andStride];
            for (int y = 0; y < height; y++)
            {
                writer.Write(maskRow);
            }

            return stream.ToArray();
        }
    }

    static void WriteIcon(string path, List<int> sizes, List<byte[]> images)
    {
        using (FileStream stream = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);

            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < images.Count; i++)
            {
                writer.Write(images[i]);
            }
        }
    }
}
