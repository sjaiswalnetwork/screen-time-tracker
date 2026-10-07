using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Generates the Screen Time Tracker icon (.ico) with 16/24/32/48/256 px images. Loaded by tools\make-icon.ps1 (no exe needed).
public static class MakeIcon
{
    public static void Generate(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 256 };
        var entries = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bmp = Render(sizes[i]))
                entries[i] = sizes[i] >= 256 ? Png(bmp) : Bmp(bmp);
        }
        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32); bw.Write(entries[i].Length); bw.Write(offset);
                offset += entries[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) bw.Write(entries[i]);
        }
    }

    // Rounded indigo-to-teal tile with a white clock face; the first third of the dial is a filled "used time" wedge.
    static Bitmap Render(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var path = RoundRect(0.5f, 0.5f, s - 1, s - 1, s * 0.24f))
            using (var br = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Color.FromArgb(92, 110, 255), Color.FromArgb(20, 184, 166)))
                g.FillPath(br, path);
            float c = s / 2f, r = s * 0.31f;
            using (var wb = new SolidBrush(Color.White))
                g.FillEllipse(wb, c - r, c - r, r * 2, r * 2);
            using (var wedge = new SolidBrush(Color.FromArgb(255, 190, 60)))
                g.FillPie(wedge, c - r * 0.82f, c - r * 0.82f, r * 1.64f, r * 1.64f, -90, 125);
            using (var pen = new Pen(Color.FromArgb(40, 48, 90), Math.Max(1.4f, s * 0.07f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, c, c, c, c - r * 0.62f);
                g.DrawLine(pen, c, c, c + r * 0.48f, c + r * 0.18f);
            }
            float d = Math.Max(1.5f, s * 0.07f);
            using (var hub = new SolidBrush(Color.FromArgb(40, 48, 90)))
                g.FillEllipse(hub, c - d, c - d, d * 2, d * 2);
        }
        return bmp;
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] Bmp(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int rowBytes = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32); bw.Write(0);
            bw.Write(w * h * 4 + h * rowBytes); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++) { var c = bmp.GetPixel(x, y); bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A); }
            for (int y = h - 1; y >= 0; y--)
            {
                var row = new byte[rowBytes];
                for (int x = 0; x < w; x++) if (bmp.GetPixel(x, y).A < 128) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
}
