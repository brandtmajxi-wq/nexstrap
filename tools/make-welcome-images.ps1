<#
  Draws the four Welcome page landscapes from code, so they carry no third-party licence.
  Output: src/Nexstrap/Resources/Welcome/{night,morning,afternoon,evening}.jpg
  Usage: powershell -File tools/make-welcome-images.ps1
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

public class Scene
{
    public string[] Sky; public double[] Stops;
    public double SunX, SunY, SunR; public string Sun; public double Glow = 1;
    public bool Moon; public double Stars;
    public string Haze, Near, CloudLit, CloudDark; public double Clouds; public string Water; public int Seed;
    public string Snow; public double Aurora, Lights; // Snow set = winter: snowy peaks and pines, a frozen lake, a cabin and a Christmas tree
}

public static class Painter
{
    const int W = 2560, H = 1440, OutW = 1920, OutH = 1080;

    static double[] C(string hex)
    {
        int v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new[] { (v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0 };
    }
    static double Mix(double a, double b, double t) { return a + (b - a) * t; }
    static double[] Mix(double[] a, double[] b, double t) { return new[] { Mix(a[0], b[0], t), Mix(a[1], b[1], t), Mix(a[2], b[2], t) }; }
    static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static double Smooth(double a, double b, double x) { double t = Clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); }

    static double Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
    static double Noise(double x, double y, int s)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        return Mix(Mix(Hash(xi, yi, s), Hash(xi + 1, yi, s), u), Mix(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), u), v);
    }
    static double Fbm(double x, double y, int octaves, int s)
    {
        double sum = 0, amp = 0.5, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Noise(x, y, s + i * 17);
            total += amp; amp *= 0.5; x *= 2.03; y *= 2.03;
        }
        return sum / total;
    }
    static double Ridge(double x, int s)
    {
        double sum = 0, amp = 0.55, total = 0, prev = 1;
        for (int i = 0; i < 7; i++)
        {
            double n = 1 - Math.Abs(2 * Noise(x, 3.7 + i, s + i * 31) - 1);
            n *= n;
            sum += amp * n * prev; total += amp; prev = n; amp *= 0.52; x *= 2.05;
        }
        return sum / total;
    }

    static double[] SkyAt(Scene s, double t)
    {
        for (int i = 0; i < s.Stops.Length - 1; i++)
            if (t <= s.Stops[i + 1])
                return Mix(C(s.Sky[i]), C(s.Sky[i + 1]), Smooth(s.Stops[i], s.Stops[i + 1], t));
        return C(s.Sky[s.Sky.Length - 1]);
    }

    static void Pine(double[] px, double[] tree, double[] snow, int cx, double groundY, double height, double half, double tiers, int seed)
    {
        for (int y = (int)(groundY - height); y < groundY && y < H; y++)
        {
            if (y < 0) continue;
            double rel = (y - (groundY - height)) / height;
            double saw = (rel * tiers) - Math.Floor(rel * tiers);
            double w = half * rel * (0.62 + 0.38 * saw) + 1.2;
            int a = Math.Max(0, (int)(cx - w)), b = Math.Min(W - 1, (int)(cx + w));
            for (int x = a; x <= b; x++)
            {
                var c = tree;
                if (snow != null)
                {
                    // Snow rests on the top of each tier of branches
                    double load = Smooth(0.2, 0.04, saw + (Noise(x * 0.11, y * 0.11, seed + 40) - 0.5) * 0.42) * (0.5 + 0.5 * rel);
                    c = Mix(tree, snow, load * 0.72);
                }
                int i = (y * W + x) * 3;
                for (int k = 0; k < 3; k++) px[i + k] = c[k];
            }
        }
    }

    static void Light(double[] px, double cx, double cy, double radius, double[] color, double strength)
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

    static void Box(double[] px, int x0, int y0, int x1, int y1, double[] color)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            {
                int i = (y * W + x) * 3;
                for (int k = 0; k < 3; k++) px[i + k] = color[k];
            }
    }

    // A log cabin with lit windows, and a decorated tree beside it
    static void Christmas(double[] px, Scene s, double[] shore, double[] tree, double[] snow, double[] near)
    {
        var warm = C("#FFC46B");
        var wall = Mix(C("#4A2C1D"), near, 0.35);
        int cx = (int)(W * 0.815), ground = (int)shore[cx] + 8;
        int bw = 118, bh = 78, roof = 62;

        Box(px, cx + 44, ground - bh - roof - 12, cx + 66, ground - bh - 10, wall);
        Box(px, cx + 42, ground - bh - roof - 20, cx + 68, ground - bh - roof - 10, snow);
        Box(px, cx - bw, ground - bh, cx + bw, ground + 4, wall);
        for (int y = ground - bh - roof; y <= ground - bh; y++)
        {
            double rel = (double)(y - (ground - bh - roof)) / roof;
            int w = (int)((bw + 22) * rel);
            Box(px, cx - w, y, cx + w, y + 1, snow);
        }
        foreach (int wx in new[] { cx - 68, cx + 34 })
        {
            Box(px, wx, ground - 58, wx + 38, ground - 20, warm);
            Box(px, wx + 17, ground - 58, wx + 21, ground - 20, wall);
            Box(px, wx, ground - 41, wx + 38, ground - 37, wall);
            Light(px, wx + 19, ground - 39, 120, warm, 0.5 * s.Lights);
        }
        Light(px, cx, ground - 30, 300, warm, 0.2 * s.Lights);

        int tx = (int)(W * 0.735), baseY = (int)shore[tx] + 10;
        double height = H * 0.3, half = height * 0.2;
        Pine(px, tree, snow, tx, baseY, height, half, 9, s.Seed + 2);
        var colors = new[] { C("#FF3B3B"), C("#FFC93B"), C("#3BE07A"), C("#3BA7FF"), C("#FF6BD6") };
        var rand = new Random(s.Seed + 5);
        for (int n = 0; n < 62; n++)
        {
            double rel = 0.08 + 0.88 * rand.NextDouble();
            double x = tx + (rand.NextDouble() * 2 - 1) * half * rel * 0.82;
            double y = baseY - height + height * rel;
            var color = colors[n % colors.Length];
            Light(px, x, y, 26, color, 0.75 * s.Lights);
            Light(px, x, y, 6, new[] { 1.0, 1.0, 1.0 }, 1);
        }
        Light(px, tx, baseY - height, 70, C("#FFD76B"), 0.9 * s.Lights);
        Light(px, tx, baseY - height, 13, new[] { 1.0, 0.97, 0.8 }, 1);
    }

    public static void Paint(Scene s, string path)
    {
        var px = new double[W * H * 3];
        bool winter = s.Snow != null;
        var snow = winter ? C(s.Snow) : null;
        int water = (int)(H * 0.76);
        double sx = s.SunX * W, sy = s.SunY * H, sr = s.SunR * H;
        var sun = C(s.Sun); var haze = C(s.Haze); var near = C(s.Near);
        var lit = C(s.CloudLit); var dark = C(s.CloudDark); var waterTint = C(s.Water);

        // Sky, stars, clouds and the sun or moon
        for (int y = 0; y < water; y++)
        {
            double t = (double)y / water;
            for (int x = 0; x < W; x++)
            {
                var c = SkyAt(s, t);
                double d = Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)) / H;
                double glow = (Math.Exp(-d * d * 70) * 0.65 + Math.Exp(-d * 4.5) * 0.32) * s.Glow;

                if (s.Stars > 0)
                {
                    double band = Fbm(x * 0.0016 + y * 0.0011, y * 0.0024 - x * 0.0007, 5, s.Seed + 9);
                    double fade = Math.Pow(1 - t, 1.4) * s.Stars;
                    double milky = Smooth(0.5, 0.74, band) * 0.16 * fade;
                    for (int k = 0; k < 3; k++) c[k] += milky * (0.8 + 0.1 * k);
                    int gx = x / 3, gy = y / 3;
                    double h = Hash(gx, gy, s.Seed);
                    double chance = 0.9962 - Smooth(0.5, 0.74, band) * 0.008;
                    if (h > chance && x % 3 == 1 && y % 3 == 1)
                    {
                        double b = (0.35 + 0.65 * Hash(gx, gy, s.Seed + 1)) * fade;
                        for (int k = 0; k < 3; k++) c[k] = Math.Max(c[k], b);
                    }
                }

                if (s.Aurora > 0)
                {
                    double centre = 0.3 + (Fbm(x * 0.0014, 1.3, 4, s.Seed + 70) - 0.5) * 0.9;
                    double band = Math.Exp(-Math.Pow((t - centre) / 0.13, 2));
                    double rays = Smooth(0.34, 0.68, Fbm(x * 0.021 + (t - centre) * 3.5, y * 0.0009, 5, s.Seed + 71));
                    double glowAmount = band * (0.3 + 0.7 * rays) * s.Aurora;
                    var hue = Mix(new[] { 0.58, 0.28, 0.95 }, new[] { 0.16, 0.95, 0.62 }, Smooth(centre - 0.14, centre + 0.03, t));
                    for (int k = 0; k < 3; k++) c[k] = 1 - (1 - c[k]) * (1 - hue[k] * glowAmount);
                }

                double px0 = d * H;
                if (s.Moon)
                {
                    double edge = Smooth(sr + 1.5, sr - 1.5, px0);
                    if (edge > 0)
                    {
                        double m = Fbm(x * 0.018, y * 0.018, 5, s.Seed + 3);
                        var moon = Mix(sun, new[] { 0.62, 0.64, 0.72 }, Smooth(0.46, 0.66, m) * 0.6);
                        c = Mix(c, moon, edge);
                    }
                }
                double wx = x * 0.0011 + 0.6 * Fbm(x * 0.0012, y * 0.003, 3, s.Seed + 5);
                double n = Fbm(wx, y * 0.0052, 6, s.Seed + 7);
                double cover = Smooth(0.47, 0.68, n) * s.Clouds * (0.45 + 0.55 * Smooth(0.05, 0.75, t));
                double light = Clamp(Smooth(0.5, 0.72, n) * 0.35 + glow * 1.4 + (1 - t) * 0.2);
                c = Mix(c, Mix(dark, lit, light), cover);

                // The sun shines through the clouds rather than sitting behind them
                for (int k = 0; k < 3; k++) c[k] = 1 - (1 - c[k]) * (1 - sun[k] * glow);
                if (!s.Moon)
                {
                    double disc = Smooth(sr + 2.5, sr - 2.5, px0);
                    c = Mix(c, new[] { 1.0, 1.0, 0.98 }, disc);
                }

                int i = (y * W + x) * 3;
                px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2];
            }
        }

        // Mountain ranges, far to near, fading into the haze
        const int layers = 6;
        for (int l = 0; l < layers; l++)
        {
            double depth = (l + 1.0) / layers;
            var color = Mix(haze, near, Math.Pow(depth, 1.25));
            double baseY = water - H * Mix(0.15, 0.015, depth);
            double amp = H * Mix(0.33, 0.085, depth);
            double freq = Mix(0.0011, 0.0034, depth);
            var ridge = new double[W];
            for (int x = 0; x < W; x++) ridge[x] = baseY - amp * Ridge(x * freq + l * 41.3, s.Seed + l * 101);

            // Light follows the broad shape of each slope, broken up by diagonal rock faces.
            // Using the raw ridge here gives one streak per pixel column.
            var broad = new double[W];
            const int reach = 70;
            for (int x = 0; x < W; x++)
            {
                double sum = 0; int count = 0;
                for (int j = Math.Max(0, x - reach); j <= Math.Min(W - 1, x + reach); j += 5) { sum += ridge[j]; count++; }
                broad[x] = sum / count;
            }

            for (int x = 0; x < W; x++)
            {
                double r = ridge[x];
                int ahead = Math.Min(W - 1, x + 24), behind = Math.Max(0, x - 24);
                double side = Math.Tanh((sx - x) / 320.0);
                double slope = (broad[ahead] - broad[behind]) / (ahead - behind) * side;
                int top = Math.Max(0, (int)r);
                for (int y = top; y < water; y++)
                {
                    double down = (y - r) / Math.Max(1, water - r);
                    double mist = Smooth(0.2, 1, down) * Mix(0.72, 0.28, depth);
                    double rock = Fbm((x + side * (y - r) * 0.7) * 0.007, (y - r) * 0.0045 + l * 7, 5, s.Seed + l + 60);
                    double tex = (Fbm(x * 0.012, y * 0.02, 4, s.Seed + l) - 0.5) * 0.07 * (1 - depth * 0.5);
                    var c = Mix(color, haze, mist);
                    if (winter)
                    {
                        double cap = Smooth(0.5, 0.04, down * Mix(1.0, 2.2, depth) + (0.5 - rock) * 1.1) * Mix(1.0, 0.4, depth);
                        c = Mix(c, Mix(snow, haze, mist * 0.7), cap);
                    }
                    double shine = Clamp(slope * 2.2 + 0.12) * Smooth(0.38, 0.68, rock) * (winter ? 0.14 : 0.42) * (1 - down) * (1 - depth * 0.6);
                    double shade = Smooth(0.62, 0.38, rock) * (winter ? 0.2 : 0.1) * (1 - down) * (1 - depth * 0.5);
                    tex -= shade;
                    double a = y == top ? 1 - (r - Math.Floor(r)) : 1;
                    int i = (y * W + x) * 3;
                    for (int k = 0; k < 3; k++)
                        px[i + k] = Mix(px[i + k], Clamp(c[k] + tex + sun[k] * shine), a);
                }
            }
        }

        // Lake: a rippled, darker mirror of everything above it
        for (int y = water; y < H; y++)
        {
            double down = (double)(y - water) / (H - water);
            for (int x = 0; x < W; x++)
            {
                double rip = Noise(x * 0.0025, y * 0.09, s.Seed + 21) - 0.5;
                double rip2 = Noise(x * 0.004, y * 0.32, s.Seed + 22) - 0.5;
                int mx = Math.Min(W - 1, Math.Max(0, (int)(x + rip * 22 * (0.25 + down))));
                int my = Math.Min(water - 1, Math.Max(0, (int)(water - (y - water) * 1.25 - 3 + (rip2 * 34 + rip * 20) * (0.3 + down))));
                int from = (my * W + mx) * 3, i = (y * W + x) * 3;
                double spread = 70 + 340 * down;
                double column = Math.Exp(-Math.Pow((x - sx) / spread, 2));
                double glint = Smooth(0.6, 0.86, Noise(x * 0.012, y * 0.21, s.Seed + 23)) * column * s.Glow;
                if (winter)
                {
                    // Frozen: a smooth, dull reflection with drifts of snow lying on the ice
                    int still = (Math.Min(water - 1, Math.Max(0, (int)(water - (y - water) * 1.25 - 3))) * W + x) * 3;
                    double drift = Smooth(0.46, 0.6, Fbm(x * 0.0022, y * 0.011, 5, s.Seed + 24)) * (0.45 + 0.55 * down);
                    double sheen = column * 0.16 * s.Glow;
                    for (int k = 0; k < 3; k++)
                    {
                        double ice = Mix(px[still + k], waterTint[k], 0.42);
                        px[i + k] = Clamp(Mix(ice, snow[k] * 0.9, drift) + sun[k] * sheen);
                    }
                    continue;
                }
                for (int k = 0; k < 3; k++)
                {
                    double m = Mix(px[from + k], waterTint[k], 0.2 + 0.32 * down);
                    px[i + k] = Clamp(m + sun[k] * glint * 0.75);
                }
            }
        }

        // Dark pines on the shores either side
        var shore = new double[W];
        var tree = Mix(near, new[] { 0.0, 0.0, 0.0 }, 0.45);
        for (int x = 0; x < W; x++)
        {
            double e = Math.Abs((double)x / W - 0.5) * 2;
            shore[x] = H * (1.04 - 0.27 * Math.Pow(e, 2.6)) + (Fbm(x * 0.006, 1.2, 4, s.Seed + 30) - 0.5) * 34;
            for (int y = Math.Max(0, (int)shore[x]); y < H; y++)
            {
                int i = (y * W + x) * 3;
                var ground = winter ? Mix(snow, near, 0.12 + 0.2 * Fbm(x * 0.004, y * 0.01, 4, s.Seed + 31)) : tree;
                for (int k = 0; k < 3; k++) px[i + k] = ground[k];
            }
        }
        var rand = new Random(s.Seed);
        for (int n = 0; n < 230; n++)
        {
            double side = rand.NextDouble() < 0.5 ? -1 : 1;
            double e = Math.Pow(rand.NextDouble(), 0.55);
            int cx = (int)(W * (0.5 + side * e * 0.52));
            if (cx < 0 || cx >= W) continue;
            double height = H * (0.07 + 0.2 * rand.NextDouble()) * (0.35 + 1.1 * Math.Pow(e, 2));
            double half = height * (0.15 + 0.05 * rand.NextDouble());
            double groundY = shore[cx] + 12, tiers = 7 + rand.Next(5);
            if (winter)
                groundY = Math.Max(shore[Math.Max(0, cx - (int)half)], shore[Math.Min(W - 1, cx + (int)half)]) + 10;
            Pine(px, tree, winter ? snow : null, cx, groundY, height, half, tiers, s.Seed);
        }

        if (winter)
            for (int x = 0; x < W; x++)
                for (int y = Math.Max(0, (int)shore[x] + 5); y < H; y++)
                {
                    int i = (y * W + x) * 3;
                    var ground = Mix(snow, near, 0.1 + 0.3 * Fbm(x * 0.004, y * 0.012, 4, s.Seed + 31));
                    for (int k = 0; k < 3; k++) px[i + k] = ground[k];
                }

        if (winter) Christmas(px, s, shore, tree, snow, near);

        // Soft vignette and a little grain so the gradients don't band
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
            var lockData = big.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bytes, 0, lockData.Scan0, bytes.Length);
            big.UnlockBits(lockData);
            using (var small = new Bitmap(OutW, OutH, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(big, 0, 0, OutW, OutH);
                }
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
  night = [Scene]@{ Sky = '#040716','#0B1538','#1C2C63','#34477F'; Stops = 0, 0.45, 0.85, 1; SunX = 0.74; SunY = 0.2; SunR = 0.052; Sun = '#F4F1E4'; Glow = 0.55; Moon = $true; Stars = 1
    Haze = '#26386B'; Near = '#060A1A'; CloudLit = '#5C6FA8'; CloudDark = '#141F47'; Clouds = 0.42; Water = '#081030'; Seed = 11 }
  morning = [Scene]@{ Sky = '#3E63B4','#8FA8DB','#FBC4A4','#FFE3B4'; Stops = 0, 0.42, 0.82, 1; SunX = 0.66; SunY = 0.4; SunR = 0.036; Sun = '#FFD9A0'; Glow = 1; Stars = 0
    Haze = '#F3C3AE'; Near = '#2B2A52'; CloudLit = '#FFE9D6'; CloudDark = '#B48FB0'; Clouds = 0.75; Water = '#6C78AE'; Seed = 23 }
  afternoon = [Scene]@{ Sky = '#155FB8','#3E97E0','#A8DBF2','#DCF2F4'; Stops = 0, 0.45, 0.85, 1; SunX = 0.77; SunY = 0.13; SunR = 0.03; Sun = '#FFF6D6'; Glow = 1.05; Stars = 0
    Haze = '#B3DCE8'; Near = '#123F33'; CloudLit = '#FFFFFF'; CloudDark = '#A9C4DC'; Clouds = 0.95; Water = '#1D6E8F'; Seed = 37 }
  evening = [Scene]@{ Sky = '#170D3C','#552676','#DE4F6A','#FFB25E'; Stops = 0, 0.4, 0.8, 1; SunX = 0.74; SunY = 0.5; SunR = 0.042; Sun = '#FFC27A'; Glow = 1.1; Stars = 0.55
    Haze = '#D85C76'; Near = '#150A28'; CloudLit = '#FFB184'; CloudDark = '#4A2160'; Clouds = 0.85; Water = '#4A1E55'; Seed = 41 }
}
$scenes['winter-night'] = [Scene]@{ Sky = '#030616','#081A3C','#0F3A5C','#2A5F80'; Stops = 0, 0.45, 0.85, 1; SunX = 0.8; SunY = 0.14; SunR = 0.04; Sun = '#F4F4EC'; Glow = 0.45; Moon = $true; Stars = 1; Aurora = 0.72
    Haze = '#2C5A7E'; Near = '#071426'; CloudLit = '#5C7FA8'; CloudDark = '#0F2747'; Clouds = 0.22; Water = '#14304F'; Seed = 53; Snow = '#8FA9CC'; Lights = 1 }
$scenes['winter-morning'] = [Scene]@{ Sky = '#6488C8','#AEC2E6','#F8D2C4','#FFEAD4'; Stops = 0, 0.42, 0.82, 1; SunX = 0.62; SunY = 0.42; SunR = 0.036; Sun = '#FFE2B8'; Glow = 1; Stars = 0
    Haze = '#EFD0CC'; Near = '#37406B'; CloudLit = '#FFF2E6'; CloudDark = '#B7A6C8'; Clouds = 0.7; Water = '#A8B6DA'; Seed = 61; Snow = '#F4E6E2'; Lights = 0.6 }
$scenes['winter-afternoon'] = [Scene]@{ Sky = '#2F78C8','#78B4E6','#CFE8F5','#EEF8FB'; Stops = 0, 0.45, 0.85, 1; SunX = 0.72; SunY = 0.14; SunR = 0.03; Sun = '#FFF8E4'; Glow = 1.05; Stars = 0
    Haze = '#CFE4EE'; Near = '#183C4C'; CloudLit = '#FFFFFF'; CloudDark = '#B7CDE2'; Clouds = 0.85; Water = '#9CC6DC'; Seed = 67; Snow = '#E6EEF5'; Lights = 0.45 }
$scenes['winter-evening'] = [Scene]@{ Sky = '#111645','#44297A','#C04F7E','#FFAE7E'; Stops = 0, 0.4, 0.8, 1; SunX = 0.66; SunY = 0.52; SunR = 0.042; Sun = '#FFC88E'; Glow = 1.05; Stars = 0.6
    Haze = '#C96487'; Near = '#1A1436'; CloudLit = '#FFBFA0'; CloudDark = '#3F2468'; Clouds = 0.75; Water = '#6A4C8C'; Seed = 71; Snow = '#EFC0CC'; Lights = 1 }

foreach ($name in $scenes.Keys) {
  $file = [IO.Path]::GetFullPath((Join-Path $out "$name.jpg"))
  [Painter]::Paint($scenes[$name], $file)
  "{0}: {1} KB" -f $name, [math]::Round((Get-Item $file).Length / 1KB)
}
