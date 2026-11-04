using Nexstrap.Core;
using System.Globalization;
using System.Text.RegularExpressions;
using Nexstrap.Models;

namespace Nexstrap.Services.FastFlags;

/// <summary>
/// Naming rules, validation and descriptions for FastFlags.
///
/// Since 29 Sep 2025 the Roblox Player only honours locally configured flags that are on
/// Roblox's allowlist; everything else in ClientAppSettings.json is ignored (no penalty).
/// The allowlist below was checked against Roblox's announcement on 3 Nov 2026 and can change
/// at any time. Roblox doesn't document what each flag does; descriptions reflect common usage.
/// </summary>
public static partial class FastFlagCatalog
{
    public const string AllowlistUrl = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";
    public const string AllowlistCheckedOn = "3 November 2026";

    public const int MaxStringLength = 1024;

    public static IReadOnlyList<KnownFlag> Known { get; } =
    [
        // Geometry
        new("DFIntCSGLevelOfDetailSwitchingDistance", FastFlagKind.Int, "Geometry",
            "Distance (in studs) at which unions and meshes start switching to lower-detail versions.", true, "0"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL12", FastFlagKind.Int, "Geometry",
            "Distance for switching from detail level 1 to 2.", true, "0"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL23", FastFlagKind.Int, "Geometry",
            "Distance for switching from detail level 2 to 3.", true, "0"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL34", FastFlagKind.Int, "Geometry",
            "Distance for switching from detail level 3 to 4.", true, "0"),

        // Rendering
        new("FFlagHandleAltEnterFullscreenManually", FastFlagKind.Bool, "Rendering",
            "Controls how Alt+Enter fullscreen is handled. Setting it to False is the usual way to get exclusive fullscreen.", true, "False"),
        new("DFFlagTextureQualityOverrideEnabled", FastFlagKind.Bool, "Rendering",
            "Turns on the texture quality override below.", true, "True"),
        new("DFIntTextureQualityOverride", FastFlagKind.Int, "Rendering",
            "Forces a texture quality level (0 = lowest, 3 = highest). Needs DFFlagTextureQualityOverrideEnabled = True.", true, "3"),
        new("FIntDebugForceMSAASamples", FastFlagKind.Int, "Rendering",
            "Forces the anti-aliasing sample count (0, 1, 2, 4 or 8).", true, "4"),
        new("DFFlagDisableDPIScale", FastFlagKind.Bool, "Rendering",
            "Ignores Windows display scaling and renders at your screen's native resolution.", true, "True"),
        new("FFlagDebugGraphicsPreferD3D11", FastFlagKind.Bool, "Rendering",
            "Prefer the Direct3D 11 renderer.", true, "True"),
        new("FFlagDebugGraphicsPreferVulkan", FastFlagKind.Bool, "Rendering",
            "Prefer the Vulkan renderer.", true, "True"),
        new("FFlagDebugGraphicsPreferOpenGL", FastFlagKind.Bool, "Rendering",
            "Prefer the OpenGL renderer.", true, "True"),
        new("FFlagDebugSkyGray", FastFlagKind.Bool, "Rendering",
            "Replaces the sky with flat gray.", true, "True"),
        new("DFFlagDebugPauseVoxelizer", FastFlagKind.Bool, "Rendering",
            "Pauses the lighting voxelizer, so lighting stops updating. Can make lighting look wrong.", true, "True"),
        new("DFIntDebugFRMQualityLevelOverride", FastFlagKind.Int, "Rendering",
            "Forces the automatic graphics quality level (higher = more detail).", true, "1"),
        new("FIntFRMMaxGrassDistance", FastFlagKind.Int, "Rendering",
            "Furthest distance grass is drawn. 0 hides grass (together with the minimum below).", true, "0"),
        new("FIntFRMMinGrassDistance", FastFlagKind.Int, "Rendering",
            "Closest distance grass starts fading in.", true, "0"),

        // User interface
        new("FIntGrassMovementReducedMotionFactor", FastFlagKind.Int, "User interface",
            "How much grass sways. 0 stops the movement.", true, "0"),

        // Popular but no longer honoured
        new("DFIntTaskSchedulerTargetFps", FastFlagKind.Int, "Not on allowlist",
            "Old frame-rate cap tweak.", false, "240",
            "Ignored by Roblox since the allowlist. Use Roblox's in-game Settings → Maximum Frame Rate instead."),
        new("FFlagDisablePostFx", FastFlagKind.Bool, "Not on allowlist",
            "Old tweak to turn off post-processing effects.", false, "True", "Ignored by Roblox since the allowlist."),
        new("FIntRenderShadowIntensity", FastFlagKind.Int, "Not on allowlist",
            "Old tweak to change shadow strength.", false, "0", "Ignored by Roblox since the allowlist."),
    ];

    private static readonly Dictionary<string, KnownFlag> ByName =
        Known.ToDictionary(k => k.Name, StringComparer.Ordinal);

    public static KnownFlag? Find(string name) => ByName.GetValueOrDefault(name.Trim());

    /// <summary>On Roblox's published allowlist, so the client actually applies it.</summary>
    public static bool IsAllowlisted(string name) => Find(name)?.Allowlisted == true;

    /// <summary>Kind from the prefix (FFlag/DFFlag/SFFlag → bool, …Int/…Log → int, …String → string).</summary>
    public static FastFlagKind? KindOf(string name)
    {
        var m = NamePattern().Match(name.Trim());
        if (!m.Success) return null;
        return m.Groups["type"].Value switch
        {
            "Flag" => FastFlagKind.Bool,
            "Int" or "Log" => FastFlagKind.Int,
            "String" => FastFlagKind.String,
            _ => null
        };
    }

    /// <returns>An error message, or null if the name is acceptable.</returns>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Loc.Get("FastFlags.EnterAFlagName");
        if (name.Trim().Length > 200) return Loc.Get("FastFlags.ThatNameIsTooLong");
        if (KindOf(name) is null)
            return Loc.Get("FastFlags.FlagNamesStartWithFFlagDFFlag");
        return null;
    }

    /// <returns>An error message, or null if <paramref name="value"/> fits the flag's kind.</returns>
    public static string? ValidateValue(string name, string? value)
    {
        value ??= "";
        switch (KindOf(name))
        {
            case FastFlagKind.Bool when !bool.TryParse(value.Trim(), out _):
                return Loc.Get("FastFlags.ThisFlagNeedsTrueOrFalse");
            case FastFlagKind.Int when !int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _):
                return Loc.Get("FastFlags.ThisFlagNeedsAWholeNumber");
            case FastFlagKind.String when value.Length > MaxStringLength:
                return Loc.Format("FastFlags.TextValuesAreLimitedToCharacters", MaxStringLength);
            case null:
                return ValidateName(name);
            default:
                return null;
        }
    }

    /// <summary>Canonical text for storage and for Roblox: "True"/"False", plain integers, strings as-is.</summary>
    public static string Normalise(string name, string value) => KindOf(name) switch
    {
        FastFlagKind.Bool => bool.Parse(value.Trim()) ? "True" : "False",
        FastFlagKind.Int => int.Parse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => value
    };

    [GeneratedRegex(@"^(?:D|S|DS)?F(?<type>Flag|Int|String|Log)[A-Za-z0-9_]+$")]
    private static partial Regex NamePattern();
}
