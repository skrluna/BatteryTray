using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

class IconTest
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    const int LOW_PCT = 20;

    static Icon MakeIcon(int pct, bool charging, bool stale)
    {
        Color ink = stale ? Color.FromArgb(130, 130, 130)
            : pct < 0 ? Color.FromArgb(110, 110, 110)
            : pct <= LOW_PCT ? Color.FromArgb(230, 60, 60)
            : pct <= 50 ? Color.FromArgb(235, 170, 30)
            : Color.FromArgb(40, 180, 90);
        using (var big = new Bitmap(48, 48))
        using (var g = Graphics.FromImage(big))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddArc(4, 14, 12, 12, 180, 90);
                path.AddArc(26, 14, 12, 12, 270, 90);
                path.AddArc(26, 22, 12, 12, 0, 90);
                path.AddArc(4, 22, 12, 12, 90, 90);
                path.CloseFigure();
                using (var dark = new SolidBrush(Color.FromArgb(50, 50, 50))) g.FillPath(dark, path);
                using (var p = new Pen(ink, 4f)) g.DrawPath(p, path);
            }
            using (var cap = new SolidBrush(ink)) g.FillRectangle(cap, 41, 19, 5, 10);
            if (pct > 0)
            {
                int w = (int)Math.Round(26 * pct / 100.0);
                if (w < 4) w = 4;
                using (var fb = new SolidBrush(ink)) g.FillRectangle(fb, 8, 18, w, 12);
            }
            if (charging && !stale)
            {
                Point[] bolt = new Point[] {
                    new Point(25, 15), new Point(17, 26), new Point(22, 26),
                    new Point(20, 33), new Point(29, 22), new Point(24, 22), new Point(27, 15) };
                using (var sb = new SolidBrush(Color.FromArgb(255, 235, 90))) g.FillPolygon(sb, bolt);
            }
            using (var small = new Bitmap(16, 16))
            using (var g2 = Graphics.FromImage(small))
            {
                g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g2.DrawImage(big, new Rectangle(0, 0, 16, 16), new Rectangle(0, 0, 48, 48), GraphicsUnit.Pixel);
                IntPtr hi = small.GetHicon();
                Icon ic = Icon.FromHandle(hi);
                Icon clone = (Icon)ic.Clone();
                DestroyIcon(hi);
                ic.Dispose();
                return clone;
            }
        }
    }

    static void Main()
    {
        int[] ps = { 75, 75, 35, 12, 62, -1 };
        bool[] cs = { false, true, false, true, false, false };
        bool[] ss = { false, false, false, false, true, false };
        string[] labels = { "75", "75chg", "35", "12chg", "stale", "nodata" };
        using (var strip = new Bitmap(600, 130))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(40, 40, 40));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var f = new Font("Segoe UI", 10f))
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                for (int i = 0; i < 6; i++)
                {
                    using (var ic = MakeIcon(ps[i], cs[i], ss[i]))
                    using (var bmp = ic.ToBitmap())
                        g.DrawImage(bmp, new Rectangle(i * 100 + 10, 10, 80, 80), new Rectangle(0, 0, 16, 16), GraphicsUnit.Pixel);
                    g.DrawString(labels[i], f, Brushes.White, new RectangleF(i * 100f, 96f, 100f, 30f), sf);
                }
            }
            strip.Save("D:/battery/icon_preview.png", System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("saved");
    }
}