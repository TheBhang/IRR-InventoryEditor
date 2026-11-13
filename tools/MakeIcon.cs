// Generates assets/app.ico (16–256 px) and assets/logo.png.
// Build & run:  csc /out:MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs  &&  MakeIcon.exe assets
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static readonly Color Bg1 = Color.FromArgb(44, 48, 55), Bg2 = Color.FromArgb(22, 24, 28);
    static readonly Color Gold = Color.FromArgb(214, 168, 72);
    static readonly Color Cell = Color.FromArgb(58, 63, 72);
    static readonly Color Blue = Color.FromArgb(88, 124, 178), Ammo = Color.FromArgb(196, 158, 62), Med = Color.FromArgb(186, 72, 72), Green = Color.FromArgb(86, 140, 90);

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float pad = s <= 16 ? 0.5f : s * 0.04f;
            var outer = new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad);
            using (var path = Round(outer, s * 0.18f))
            {
                using (var b = new LinearGradientBrush(outer, Bg1, Bg2, LinearGradientMode.Vertical)) g.FillPath(b, path);
                using (var pen = new Pen(Gold, Math.Max(1f, s * 0.045f))) g.DrawPath(pen, path);
            }
            // 3x3 inventory grid (2x2 on the tiniest size), tiles like the editor
            int n = s <= 16 ? 2 : 3;
            float inner = s * (s <= 16 ? 0.62f : 0.62f);
            float gap = Math.Max(1f, s * 0.035f);
            float cell = (inner - gap * (n - 1)) / n;
            float ox = (s - inner) / 2f, oy = (s - inner) / 2f + (s <= 16 ? 0 : s * 0.01f);
            Func<int, int, int, int, RectangleF> R = (c, r, w, h) =>
                new RectangleF(ox + c * (cell + gap), oy + r * (cell + gap), w * cell + (w - 1) * gap, h * cell + (h - 1) * gap);
            float cr = Math.Max(0.5f, cell * 0.16f);
            using (var cb = new SolidBrush(Cell))
                for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) using (var p = Round(R(c, r, 1, 1), cr)) g.FillPath(cb, p);
            Action<RectangleF, Color> tile = (rc, col) =>
            {
                var light = Color.FromArgb(Math.Min(255, col.R + 40), Math.Min(255, col.G + 40), Math.Min(255, col.B + 40));
                using (var p = Round(rc, cr))
                using (var b = new LinearGradientBrush(rc, light, col, LinearGradientMode.Vertical)) g.FillPath(b, p);
            };
            if (n == 3)
            {
                tile(R(0, 0, 2, 1), Blue);   // weapon (2 wide)
                tile(R(2, 0, 1, 2), Med);    // med (2 tall)
                tile(R(0, 1, 1, 1), Ammo);   // ammo
                tile(R(0, 2, 2, 1), Green);  // gear
                // selected tile outline – the editor's gold selection
                using (var p = Round(R(1, 1, 1, 1), cr)) using (var pen = new Pen(Gold, Math.Max(1f, s * 0.03f))) g.DrawPath(pen, p);
            }
            else
            {
                tile(R(0, 0, 2, 1), Blue);
                tile(R(0, 1, 1, 1), Ammo);
            }
        }
        return bmp;
    }

    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
            using (var b = Draw(s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
        // ICO container with PNG-compressed entries (supported since Vista)
        using (var fs = File.Create(Path.Combine(outDir, "app.ico")))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset); offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        using (var b = Draw(256)) b.Save(Path.Combine(outDir, "logo.png"), ImageFormat.Png);
        Console.WriteLine("icon written to " + outDir);
    }
}
