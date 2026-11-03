using System.Runtime.InteropServices;
using Nexstrap.Models;
using Nexstrap.Services.FastFlags;

namespace Nexstrap.Services.Performance;

public interface IDisplayInfo
{
    /// <summary>Primary display's refresh rate in Hz, or null if Windows doesn't report one.</summary>
    int? RefreshRate { get; }
}

public sealed class DisplayInfo : IDisplayInfo
{
    private const int CurrentSettings = -1;

    public int? RefreshRate
    {
        get
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            // 0 and 1 are Windows' "hardware default" placeholders, not real rates.
            return EnumDisplaySettings(null, CurrentSettings, ref mode) && mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}

public enum GraphicsApi { Automatic, Direct3D11, Vulkan, OpenGL }

public enum StatusLevel { Good, Info, Warning }

public sealed record StatusLine(StatusLevel Level, string Text);

/// <summary>Null cap means "the highest standard cap your display can show".</summary>
public sealed record PerformancePreset(string Name, string Description, int? Cap, int Quality);

/// <summary>
/// Pure decisions for the Performance page. Frame rate and quality use Roblox's own settings;
/// renderer, anti-aliasing and textures use allowlisted FastFlags, which need a Nexstrap-installed build.
/// </summary>
public static class PerformancePlanner
{
    public static readonly int[] StandardCaps = [60, 120, 144, 240];

    public const string ForceMsaa = "FIntDebugForceMSAASamples";
    public const string TextureOverrideEnabled = "DFFlagTextureQualityOverrideEnabled";
    public const string TextureOverride = "DFIntTextureQualityOverride";
    public const string OldFpsFlag = "DFIntTaskSchedulerTargetFps";

    private static readonly Dictionary<GraphicsApi, string> ApiFlags = new()
    {
        [GraphicsApi.Direct3D11] = "FFlagDebugGraphicsPreferD3D11",
        [GraphicsApi.Vulkan] = "FFlagDebugGraphicsPreferVulkan",
        [GraphicsApi.OpenGL] = "FFlagDebugGraphicsPreferOpenGL"
    };

    public static IReadOnlyList<PerformancePreset> Presets { get; } =
    [
        new("Balanced", "Roblox picks the quality automatically, capped to your display.", null, 0),
        new("Smoothest", "Low fixed quality, capped to your display. Best chance of steady frames on slower PCs.", null, 3),
        new("Best looking", "Highest fixed quality, capped to your display. Needs a strong PC.", null, 10),
        new("Power saver", "Medium quality at 60 FPS. Less heat, fan noise and battery use.", 60, 5)
    ];

    /// <summary>Highest standard cap the display can actually show; 60 if unknown.</summary>
    public static int RecommendedCap(int? refreshRate)
    {
        // Rates like 59.94 Hz are reported as 59, and still suit a 60 cap.
        var usable = StandardCaps.Where(c => c <= (refreshRate ?? 60) + 1).ToList();
        return usable.Count > 0 ? usable.Max() : StandardCaps[0];
    }

    public static int ResolveCap(PerformancePreset preset, int? refreshRate) => preset.Cap ?? RecommendedCap(refreshRate);

    // ===== Flag-backed preferences =====

    public static GraphicsApi ReadApi(IEnumerable<FastFlagEntry> flags)
    {
        foreach (var (api, name) in ApiFlags)
            if (flags.Any(f => f.Name == name && f.Enabled && f.Value == "True")) return api;
        return GraphicsApi.Automatic;
    }

    public static void ApplyApi(IFastFlagService flags, GraphicsApi api)
    {
        foreach (var (candidate, name) in ApiFlags)
        {
            if (candidate == api) flags.Set(name, "True");
            else if (flags.Flags.Any(f => f.Name == name)) flags.Remove(name);
        }
    }

    /// <summary>-1 = leave to Roblox.</summary>
    public static int ReadInt(IEnumerable<FastFlagEntry> flags, string name) =>
        flags.FirstOrDefault(f => f.Name == name && f.Enabled) is { } flag && int.TryParse(flag.Value, out var value) ? value : -1;

    public static void ApplyMsaa(IFastFlagService flags, int samples)
    {
        if (samples >= 0) flags.Set(ForceMsaa, samples.ToString());
        else if (flags.Flags.Any(f => f.Name == ForceMsaa)) flags.Remove(ForceMsaa);
    }

    public static void ApplyTextures(IFastFlagService flags, int level)
    {
        if (level >= 0)
        {
            flags.Set(TextureOverrideEnabled, "True");
            flags.Set(TextureOverride, level.ToString());
            return;
        }
        foreach (var name in new[] { TextureOverrideEnabled, TextureOverride })
            if (flags.Flags.Any(f => f.Name == name)) flags.Remove(name);
    }

    // ===== Status =====

    public static IReadOnlyList<StatusLine> BuildStatus(int? cap, int? refreshRate, bool settingsFileExists, bool robloxRunning,
        bool playUsesNexstrapBuild, IReadOnlyList<FastFlagEntry> flags)
    {
        var lines = new List<StatusLine>();

        if (!settingsFileExists)
            lines.Add(new(StatusLevel.Warning, "Roblox hasn't created its settings file yet. Start Roblox once, then come back."));
        else if (cap is null)
            lines.Add(new(StatusLevel.Info, "No frame-rate cap saved yet, so Roblox uses its default."));
        else if (refreshRate is { } hz && cap > hz + 1)
            lines.Add(new(StatusLevel.Info, $"Cap is {cap} FPS but your display shows {hz} Hz. Frames above {hz} can't appear on screen."));
        else if (refreshRate is { } rate && cap < RecommendedCap(rate))
            lines.Add(new(StatusLevel.Info, $"Cap is {cap} FPS. Your {rate} Hz display can show up to {RecommendedCap(rate)}."));
        else
            lines.Add(new(StatusLevel.Good, $"Cap is {cap} FPS, a good match for your display."));

        if (robloxRunning)
            lines.Add(new(StatusLevel.Warning, "Roblox is open. Close it to change the frame-rate cap or graphics quality."));

        if (flags.Any(f => f.Name == OldFpsFlag && f.Enabled))
            lines.Add(new(StatusLevel.Warning, $"{OldFpsFlag} is in your FastFlags, but Roblox ignores it. Use the cap above instead."));

        var usesFlags = ReadApi(flags) != GraphicsApi.Automatic || ReadInt(flags, ForceMsaa) >= 0 || ReadInt(flags, TextureOverride) >= 0;
        if (usesFlags && !playUsesNexstrapBuild)
            lines.Add(new(StatusLevel.Warning, "Your graphics preferences won't apply: Play isn't using a Nexstrap-installed build. Install one from Home."));
        else if (usesFlags)
            lines.Add(new(StatusLevel.Good, "Your graphics preferences are written to Roblox each time you press Play."));

        return lines;
    }
}
