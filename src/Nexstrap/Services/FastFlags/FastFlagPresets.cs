using Nexstrap.Core;

namespace Nexstrap.Services.FastFlags;

public sealed record FastFlagPreset(string Key, IReadOnlyDictionary<string, string> Flags)
{
    public string Name => Loc.Get(Key);

    public string Description => Loc.Get(Key + ".Text");
}

/// <summary>Ready-made sets. Every flag here must be on Roblox's allowlist, which a test checks.</summary>
public static class FastFlagPresets
{
    public static IReadOnlyList<FastFlagPreset> All { get; } =
    [
        new("FastFlags.Preset.LowEnd", new Dictionary<string, string>
        {
            ["DFFlagTextureQualityOverrideEnabled"] = "True",
            ["DFIntTextureQualityOverride"] = "0",
            ["FIntDebugForceMSAASamples"] = "0",
            ["FIntFRMMaxGrassDistance"] = "0",
            ["FIntFRMMinGrassDistance"] = "0"
        }),
        new("FastFlags.Preset.Clean", new Dictionary<string, string>
        {
            ["FIntFRMMaxGrassDistance"] = "0",
            ["FIntFRMMinGrassDistance"] = "0",
            ["FFlagDebugSkyGray"] = "True"
        }),
        new("FastFlags.Preset.Sharp", new Dictionary<string, string>
        {
            ["DFFlagTextureQualityOverrideEnabled"] = "True",
            ["DFIntTextureQualityOverride"] = "3",
            ["FIntDebugForceMSAASamples"] = "4"
        })
    ];

    /// <summary>Returns the first problem, or null if every flag was set.</summary>
    public static string? Apply(IFastFlagService flags, FastFlagPreset preset)
    {
        foreach (var (name, value) in preset.Flags)
            if (flags.Set(name, value) is { } error) return error;
        return null;
    }
}
