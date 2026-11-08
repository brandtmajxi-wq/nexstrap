<#
  Draws the secret page's Moscow skylines from code (Kremlin wall and towers, the bell tower, St Basil's,
  a high-rise and the river), so they carry no third-party licence.
  Output: src/Nexstrap/Resources/Welcome/moscow-{night,morning,afternoon,evening}.jpg
  Usage: powershell -File tools/make-moscow-images.ps1
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\Nexstrap\Resources\Welcome'
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class City
{
    public string[] Sky; public double[] Stops;
    public double SunX, SunY, SunR; public string Sun; public double Glow = 1;
    public bool Moon; public double Stars;
    public string CloudLit, CloudDark; public double Clouds;
    public string Haze, Water, Light; public double Night; public int Seed;
}

public static class Moscow
{
    const int W = 2560, H = 1440, OutW = 1920, OutH = 1080;
    const int WL = 1152, Bank = 1126;
    static double[] px;
    static City s;
    static double[] light, haze, warm;

    static double[] C(string hex)
    {
        int v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new[] { (v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0 };
    }
    static double Mix(double a, double b, double t) { return a + (b - a) * t; }
    static double[] Mix(double[] a, double[] b, double t) { return new[] { Mix(a[0], b[0], t), Mix(a[1], b[1], t), Mix(a[2], b[2], t) }; }
    static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static double Smooth(double a, double b, double x) { double t = Clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
    static double Noise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi, u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        return Mix(Mix(Hash(xi, yi, seed), Hash(xi + 1, yi, seed), u), Mix(Hash(xi, yi + 1, seed), Hash(xi + 1, yi + 1, seed), u), v);
    }
    static double Fbm(double x, double y, int oct, int seed)
    {
        double sum = 0, amp = 0.5, tot = 0;
        for (int i = 0; i < oct; i++) { sum += amp * Noise(x, y, seed + i * 17); tot += amp; amp *= 0.5; x *= 2.03; y *= 2.03; }
        return sum / tot;
    }

    static void Set(int x, int y, double[] c)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        int i = (y * W + x) * 3; px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2];
    }

    static void Glow(double cx, double cy, double radius, double[] color, double strength)
    {
        int x0 = Math.Max(0, (int)(cx - radius)), x1 = Math.Min(W - 1, (int)(cx + radius));
        int y0 = Math.Max(0, (int)(cy - radius)), y1 = Math.Min(H - 1, (int)(cy + radius));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1) continue;
                double a = Math.Pow(1 - d, 2.4) * strength;
                int i = (y * W + x) * 3;
                for (int k = 0; k < 3; k++) px[i + k] = 1 - (1 - px[i + k]) * (1 - color[k] * a);
            }
    }

    /// <summary>A surface colour in this scene's light: tinted by daylight, fogged by distance, floodlit at night.</summary>
    static double[] Lit(double[] baseColor, double fog, double side, double floodUp)
    {
        var c = new double[3];
        double sideShade = 1 - 0.2 * side * (1 - s.Night);
        for (int k = 0; k < 3; k++) c[k] = baseColor[k] * light[k] * sideShade;
        if (s.Night > 0)
        {
            double flood = s.Night * 0.62 * (0.35 + 0.65 * floodUp) * (1 - fog);
            for (int k = 0; k < 3; k++) c[k] = c[k] + baseColor[k] * warm[k] * flood;
        }
        return Mix(c, haze, fog);
    }

    static double Side(double x, double cx, double half)
    {
        double sunSide = s.SunX < 0.5 ? -1 : 1;
        return Clamp(0.5 - 0.5 * sunSide * (x - cx) / Math.Max(1, half));
    }

    static void Block(int x0, int x1, int y0, int y1, string color, double fog, double windows = 0, int seed = 0)
    {
        var baseColor = C(color);
        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            {
                double up = (double)(y - y0) / Math.Max(1, y1 - y0);
                var c = Lit(baseColor, fog, Side(x, (x0 + x1) / 2.0, (x1 - x0) / 2.0), up);
                if (windows > 0)
                {
                    int gx = (x - x0) / 18, gy = (y - y0) / 24, lx = (x - x0) % 18, ly = (y - y0) % 24;
                    bool pane = lx >= 6 && lx < 12 && ly >= 7 && ly < 16 && y - y0 > 10;
                    if (pane)
                    {
                        bool on = Hash(gx, gy, seed) < windows * (0.25 + 0.75 * s.Night);
                        var paneColor = on && s.Night > 0.3 ? Mix(C("#FFD38A"), haze, fog * 0.6) : Mix(c, C("#1A2238"), 0.35 * (1 - fog));
                        c = paneColor;
                    }
                }
                Set(x, y, c);
            }
    }

    static void Tent(double cx, int yBase, int yTop, double half, string color, double fog)
    {
        var baseColor = C(color);
        for (int y = Math.Max(0, yTop); y < yBase; y++)
        {
            double t = (double)(y - yTop) / (yBase - yTop);
            double w = half * t + 0.8;
            for (int x = (int)(cx - w); x <= (int)(cx + w); x++)
            {
                double ridge = ((int)((y - yTop) / 16.0) % 2 == 0) ? 1.0 : 0.9;
                var c = Lit(new[] { baseColor[0] * ridge, baseColor[1] * ridge, baseColor[2] * ridge }, fog, Side(x, cx, w), t * 0.6);
                Set(x, y, c);
            }
        }
    }

    /// <summary>An onion dome: pointed tip, round bulb, narrow neck. Two colours spiral around it.</summary>
    static void Onion(double cx, int yBottom, int height, double r, string a, string b, double swirl, double fog)
    {
        var ca = C(a); var cb = C(b);
        int yTop = yBottom - height;
        for (int y = Math.Max(0, yTop); y < yBottom; y++)
        {
            double t = (double)(y - yTop) / height;
            double w;
            if (t < 0.18) w = r * 0.07 + 1;
            else if (t < 0.6) w = r * Math.Pow(Math.Sin((t - 0.18) / 0.42 * Math.PI / 2), 1.7);
            else w = Math.Max(r * 0.42, r * Math.Pow(Math.Cos((t - 0.6) / 0.4 * Math.PI / 2), 0.55));
            for (int x = (int)(cx - w); x <= (int)(cx + w); x++)
            {
                double ang = Math.Asin(Math.Max(-1, Math.Min(1, (x - cx) / Math.Max(1, w))));
                int stripe = (int)Math.Floor(ang / Math.PI * 7 + t * swirl * 3 + 20);
                var col = stripe % 2 == 0 ? ca : cb;
                double shade = 0.68 + 0.32 * Math.Cos(ang + (s.SunX < 0.5 ? 0.5 : -0.5));
                var c = Lit(new[] { col[0] * shade, col[1] * shade, col[2] * shade }, fog, 0, 0.5);
                double hl = Math.Exp(-Math.Pow((ang + (s.SunX < 0.5 ? -0.55 : 0.55)) / 0.18, 2)) * Smooth(0.25, 0.5, t) * Smooth(0.85, 0.6, t) * 0.35 * (1 - s.Night * 0.5);
                for (int k = 0; k < 3; k++) c[k] = Clamp(c[k] + hl);
                Set(x, y, c);
            }
        }
        // a small cross-like finial
        Block((int)cx - 2, (int)cx + 2, yTop - 26, yTop, "#E8C25A", fog);
        Block((int)cx - 8, (int)cx + 8, yTop - 20, yTop - 16, "#E8C25A", fog);
    }

    static void Star(double cx, double cy, double r)
    {
        var ruby = C("#E0182D");
        for (int y = (int)(cy - r); y <= cy + r; y++)
            for (int x = (int)(cx - r); x <= cx + r; x++)
            {
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                double a = Math.Atan2(dy, dx) + Math.PI / 2;
                double edge = r * (0.45 + 0.55 * Math.Pow(Math.Abs(Math.Cos(a * 2.5)), 6));
                if (d <= edge) Set(x, y, Mix(ruby, new[] { 1.0, 0.6, 0.6 }, s.Night * 0.35));
            }
        Glow(cx, cy, r * 6, ruby, 0.25 + 0.65 * s.Night);
    }

    static void WallTower(int cx, int top, int half, string roof, double fog)
    {
        Block(cx - half, cx + half, top + 70, Bank, "#A3342B", fog);
        Block(cx - half + 8, cx + half - 8, top, top + 70, "#A83A2F", fog);
        for (int m = cx - half; m < cx + half; m += 16) Block(m, m + 9, top + 58, top + 70, "#8E2B23", fog);
        Tent(cx, top, top - (int)(half * 3.6), half - 6, roof, fog);
        Block(cx - 2, cx + 2, top - (int)(half * 3.6) - 22, top - (int)(half * 3.6), "#E8C25A", fog);
    }

    public static void Paint(City scene, string path)
    {
        s = scene;
        px = new double[W * H * 3];
        light = C(s.Light); haze = C(s.Haze); warm = C("#FFC27A");
        var sun = C(s.Sun); var lit = C(s.CloudLit); var dark = C(s.CloudDark); var waterTint = C(s.Water);
        double sx = s.SunX * W, sy = s.SunY * H, sr = s.SunR * H;

        // Sky with stars, clouds and the sun or moon
        for (int y = 0; y < WL; y++)
        {
            double t = (double)y / WL;
            double[] baseSky = null;
            for (int i = 0; i < s.Stops.Length - 1 && baseSky == null; i++)
                if (t <= s.Stops[i + 1]) baseSky = Mix(C(s.Sky[i]), C(s.Sky[i + 1]), Smooth(s.Stops[i], s.Stops[i + 1], t));
            if (baseSky == null) baseSky = C(s.Sky[s.Sky.Length - 1]);
            for (int x = 0; x < W; x++)
            {
                var c = (double[])baseSky.Clone();
                double d = Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)) / H;
                double glow = (Math.Exp(-d * d * 70) * 0.65 + Math.Exp(-d * 4.5) * 0.32) * s.Glow;
                if (s.Stars > 0 && x % 3 == 1 && y % 3 == 1)
                {
                    double h = Hash(x / 3, y / 3, s.Seed);
                    if (h > 0.9965) { double b = (0.35 + 0.65 * Hash(x / 3, y / 3, s.Seed + 1)) * Math.Pow(1 - t, 1.4) * s.Stars; for (int k = 0; k < 3; k++) c[k] = Math.Max(c[k], b); }
                }
                double pd = d * H;
                if (s.Moon)
                {
                    double edge = Smooth(sr + 1.5, sr - 1.5, pd);
                    if (edge > 0) c = Mix(c, Mix(sun, new[] { 0.62, 0.64, 0.72 }, Smooth(0.46, 0.66, Fbm(x * 0.018, y * 0.018, 5, s.Seed + 3)) * 0.6), edge);
                }
                double n = Fbm(x * 0.0011 + 0.6 * Fbm(x * 0.0012, y * 0.003, 3, s.Seed + 5), y * 0.0052, 6, s.Seed + 7);
                double cover = Smooth(0.47, 0.68, n) * s.Clouds * (0.45 + 0.55 * Smooth(0.05, 0.75, t));
                double l = Clamp(Smooth(0.5, 0.72, n) * 0.35 + glow * 1.4 + (1 - t) * 0.2);
                c = Mix(c, Mix(dark, lit, l), cover);
                for (int k = 0; k < 3; k++) c[k] = 1 - (1 - c[k]) * (1 - sun[k] * glow);
                if (!s.Moon) c = Mix(c, new[] { 1.0, 1.0, 0.98 }, Smooth(sr + 2.5, sr - 2.5, pd));
                Set(x, y, c);
            }
        }

        // Distant city, fading into the haze
        var rand = new Random(s.Seed);
        for (int x = -20; x < W;)
        {
            int w = 40 + rand.Next(80), h = 50 + rand.Next(150);
            Block(x, x + w - 4, Bank - h, Bank, rand.NextDouble() < 0.5 ? "#6E7488" : "#7C7A86", 0.62, 0.5, s.Seed + x);
            x += w;
        }

        // A stepped high-rise with a spire and star
        int hx = 330;
        Block(hx - 190, hx + 190, 960, Bank, "#C9B99A", 0.36, 0.55, s.Seed + 90);
        Block(hx - 120, hx + 120, 800, 960, "#CDBE9F", 0.36, 0.55, s.Seed + 91);
        Block(hx - 74, hx + 74, 660, 800, "#D2C3A4", 0.36, 0.5, s.Seed + 92);
        Block(hx - 40, hx + 40, 570, 660, "#D6C8AA", 0.36, 0.3, s.Seed + 93);
        Tent(hx, 570, 390, 14, "#D9C9A0", 0.36);
        Star(hx, 382, 13);

        // Inside the Kremlin: the palace, cathedral domes and the bell tower
        Block(740, 960, 915, 1012, "#4C7A5E", 0.12);
        Block(740, 960, 930, 1012, "#E9D9A6", 0.12, 0.6, s.Seed + 40);
        Block(990, 1150, 905, 1012, "#EFE6D2", 0.12);
        foreach (var d in new[] { new[] { 1010.0, 26, 80 }, new[] { 1050.0, 30, 96 }, new[] { 1070.0, 36, 120 }, new[] { 1095.0, 30, 96 }, new[] { 1130.0, 24, 78 } })
        {
            int dx = (int)d[0], r = (int)d[1], h = (int)d[2];
            Block(dx - (int)(r * 0.75), dx + (int)(r * 0.75), 870, 905, "#EFE6D2", 0.12);
            Onion(dx, 875, h, r, "#E9BC4C", "#F2CC66", 0, 0.12);
        }
        int bx = 1215;
        Block(bx - 36, bx + 36, 650, 1012, "#F1EBDD", 0.1);
        Block(bx - 29, bx + 29, 560, 650, "#F3EDE0", 0.1);
        Block(bx - 22, bx + 22, 490, 560, "#F3EDE0", 0.1);
        for (int y = 520; y < 1000; y += 90) Block(bx - 10, bx + 10, y, y + 40, "#9AA2B0", 0.1);
        Onion(bx, 494, 96, 30, "#EDC04E", "#F6D474", 0, 0.1);

        // The red wall with swallowtail battlements
        int wallTop = 1012;
        Block(690, 1890, wallTop, Bank, "#A3342B", 0.04);
        for (int m = 690; m < 1890; m += 34)
        {
            Block(m, m + 22, wallTop - 30, wallTop, "#A3342B", 0.04);
            for (int y = wallTop - 30; y < wallTop - 18; y++)
            {
                int notch = (int)((wallTop - 18 - y) * 0.5);
                Block(m + 11 - notch, m + 11 + notch, y, y + 1, "#00000000".Length > 0 ? "#A3342B" : "#A3342B", 0.04);
            }
        }
        // Repaint the notches as sky by drawing the V gaps from the sky above them
        for (int m = 690; m < 1890; m += 34)
            for (int y = wallTop - 30; y < wallTop - 18; y++)
            {
                int notch = (int)((wallTop - 18 - y) * 0.45);
                for (int x = m + 11 - notch; x <= m + 11 + notch; x++)
                {
                    int src = ((wallTop - 40) * W + x) * 3, dst = (y * W + x) * 3;
                    if (x >= 0 && x < W) for (int k = 0; k < 3; k++) px[dst + k] = px[src + k];
                }
            }

        WallTower(740, 900, 44, "#2F6B4A", 0.05);
        WallTower(1000, 920, 36, "#2F6B4A", 0.05);
        WallTower(1700, 910, 40, "#2F6B4A", 0.05);
        WallTower(1870, 880, 48, "#2F6B4A", 0.05);

        // The clock tower over the main gate
        int cx = 1420;
        Block(cx - 66, cx + 66, 860, Bank, "#A3342B", 0.03);
        Block(cx - 22, cx + 22, 1050, Bank, "#3A1B18", 0.03);
        Block(cx - 56, cx + 56, 770, 860, "#EFE6D6", 0.03);
        for (int y = 770; y < 860; y++)
            for (int x = cx - 34; x <= cx + 34; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - 815) * (y - 815));
                if (d <= 33) Set(x, y, d > 29 ? Lit(C("#E3B64C"), 0.03, 0, 1) : Lit(C("#1F2D52"), 0.03, 0, 1));
            }
        Block(cx - 1, cx + 2, 795, 816, "#E3B64C", 0.03);
        Block(cx, cx + 18, 814, 818, "#E3B64C", 0.03);
        Block(cx - 46, cx + 46, 690, 770, "#A83A2F", 0.03);
        Block(cx - 32, cx + 32, 630, 690, "#EFE6D6", 0.03);
        Tent(cx, 630, 450, 32, "#2F6B4A", 0.03);
        Star(cx, 432, 24);

        // St Basil's: a red base, a tall central tent and bright striped onion domes
        Block(2050, 2430, 1020, Bank, "#A8372C", 0.02);
        for (int a = 2066; a < 2420; a += 38) Block(a, a + 20, 1050, 1110, "#EBDDC4", 0.02);
        Block(2205, 2275, 770, 1020, "#C8483A", 0.02);
        Tent(2240, 770, 560, 36, "#E6C35A", 0.02);
        Onion(2240, 566, 54, 15, "#E9BC4C", "#F6D474", 0, 0.02);
        var domes = new[]
        {
            new object[] { 2100, 930, 118, 36.0, "#C9302C", "#F4EDE2", 1.4 },
            new object[] { 2160, 880, 126, 34.0, "#2E8B57", "#F2C94C", -1.2 },
            new object[] { 2320, 890, 122, 35.0, "#2F64B5", "#F4EDE2", 1.0 },
            new object[] { 2385, 945, 108, 32.0, "#2E8B57", "#C9302C", -1.6 },
            new object[] { 2245, 950, 96, 30.0, "#E0A53A", "#2E8B57", 1.8 },
        };
        foreach (var d in domes)
        {
            int dx = (int)d[0], bottom = (int)d[1], h = (int)d[2]; double r = (double)d[3];
            Block(dx - (int)(r * 0.6), dx + (int)(r * 0.6), bottom - 4, 1020, "#B9443A", 0.02);
            Onion(dx, bottom, h, r, (string)d[4], (string)d[5], (double)d[6], 0.02);
        }

        // Embankment and street lights
        Block(0, W, Bank, WL, "#4B4A52", 0);
        for (int x = 40; x < W; x += 150)
        {
            Block(x - 2, x + 2, Bank - 46, Bank, "#2A2A30", 0);
            if (s.Night > 0.2) { Glow(x, Bank - 48, 70, warm, 0.8 * s.Night); Glow(x, Bank - 48, 8, new[] { 1.0, 0.95, 0.8 }, 1); }
        }

        // The river: a rippled mirror with long streaks of light at night
        for (int y = WL; y < H; y++)
        {
            double down = (double)(y - WL) / (H - WL);
            for (int x = 0; x < W; x++)
            {
                double rip = Noise(x * 0.0025, y * 0.09, s.Seed + 21) - 0.5;
                double rip2 = Noise(x * 0.004, y * 0.32, s.Seed + 22) - 0.5;
                int mx = Math.Min(W - 1, Math.Max(0, (int)(x + rip * 18 * (0.25 + down))));
                int my = Math.Min(WL - 1, Math.Max(0, (int)(WL - (y - WL) * 1.1 - 4 + (rip2 * 30 + rip * 16) * (0.3 + down))));
                int from = (my * W + mx) * 3, i = (y * W + x) * 3;
                double glint = Smooth(0.6, 0.86, Noise(x * 0.012, y * 0.21, s.Seed + 23)) * Math.Exp(-Math.Pow((x - sx) / (70 + 340 * down), 2)) * s.Glow * (s.Moon ? 0.5 : 1);
                for (int k = 0; k < 3; k++) px[i + k] = Clamp(Mix(px[from + k], waterTint[k], 0.22 + 0.3 * down) + sun[k] * glint * 0.7);
            }
        }

        // Soft vignette and grain
        var bytes = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double dx = (double)x / W - 0.5, dy = (double)y / H - 0.5;
                double vig = 1 - 0.24 * (dx * dx + dy * dy) * 2;
                double grain = (Hash(x, y, s.Seed + 50) - 0.5) * 0.014;
                int i = (y * W + x) * 3, o = (y * W + x) * 4;
                bytes[o] = (byte)(Clamp(px[i + 2] * vig + grain) * 255);
                bytes[o + 1] = (byte)(Clamp(px[i + 1] * vig + grain) * 255);
                bytes[o + 2] = (byte)(Clamp(px[i] * vig + grain) * 255);
                bytes[o + 3] = 255;
            }
        using (var big = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        {
            var data = big.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            big.UnlockBits(data);
            using (var small = new Bitmap(OutW, OutH, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(big, 0, 0, OutW, OutH); }
                var jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.MimeType == "image/jpeg");
                var options = new EncoderParameters(1);
                options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                small.Save(path, jpeg, options);
            }
        }
    }
}
'@

$scenes = @{
  night = [City]@{ Sky = '#040716','#0B1538','#1C2C63','#34477F'; Stops = 0, 0.45, 0.85, 1; SunX = 0.62; SunY = 0.16; SunR = 0.045; Sun = '#F4F1E4'; Glow = 0.5; Moon = $true; Stars = 1
    CloudLit = '#5C6FA8'; CloudDark = '#141F47'; Clouds = 0.3; Haze = '#24345F'; Water = '#081030'; Light = '#2C3558'; Night = 1; Seed = 11 }
  morning = [City]@{ Sky = '#3E63B4','#8FA8DB','#FBC4A4','#FFE3B4'; Stops = 0, 0.42, 0.82, 1; SunX = 0.3; SunY = 0.5; SunR = 0.034; Sun = '#FFD9A0'; Glow = 1; Stars = 0
    CloudLit = '#FFE9D6'; CloudDark = '#B48FB0'; Clouds = 0.7; Haze = '#E9C0B4'; Water = '#6C78AE'; Light = '#F2D2C0'; Night = 0; Seed = 23 }
  afternoon = [City]@{ Sky = '#155FB8','#3E97E0','#A8DBF2','#DCF2F4'; Stops = 0, 0.45, 0.85, 1; SunX = 0.8; SunY = 0.12; SunR = 0.028; Sun = '#FFF6D6'; Glow = 1; Stars = 0
    CloudLit = '#FFFFFF'; CloudDark = '#A9C4DC'; Clouds = 0.9; Haze = '#B9D6E4'; Water = '#2A6E8F'; Light = '#FFFFFF'; Night = 0; Seed = 37 }
  evening = [City]@{ Sky = '#170D3C','#552676','#DE4F6A','#FFB25E'; Stops = 0, 0.4, 0.8, 1; SunX = 0.18; SunY = 0.58; SunR = 0.04; Sun = '#FFC27A'; Glow = 1.1; Stars = 0.5
    CloudLit = '#FFB184'; CloudDark = '#4A2160'; Clouds = 0.8; Haze = '#B8577A'; Water = '#4A1E55'; Light = '#E8A08E'; Night = 0.45; Seed = 41 }
}
foreach ($name in $scenes.Keys) {
  $file = [IO.Path]::GetFullPath((Join-Path $out "moscow-$name.jpg"))
  [Moscow]::Paint($scenes[$name], $file)
  "{0}: {1} KB" -f $name, [math]::Round((Get-Item $file).Length / 1KB)
}
