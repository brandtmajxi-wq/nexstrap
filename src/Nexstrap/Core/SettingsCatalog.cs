namespace Nexstrap.Core;

public sealed record SettingEntry(string LabelKey, string Page);

public sealed record SettingResult(string Label, string PageLabel, string Page);

/// <summary>The options the settings search can jump to. Each label is the card's own header, so the page can scroll to it.</summary>
public static class SettingsCatalog
{
    public const int MaxResults = 8;

    public static IReadOnlyList<SettingEntry> Entries { get; } =
    [
        new("Appearance.Corners", "Appearance"),
        new("Appearance.Cards", "Appearance"),
        new("Appearance.SidebarTone", "Appearance"),
        new("Appearance.IconSidebar", "Appearance"),
        new("Appearance.GradientButtons", "Appearance"),
        new("Appearance.Transition", "Appearance"),
        new("Appearance.BrandName", "Appearance"),
        new("Appearance.BrandLogo", "Appearance"),
        new("Appearance.ShowTagline", "Appearance"),
        new("Appearance.AppSize", "Appearance"),
        new("Appearance.YourName", "Appearance"),
        new("LaunchScreen.ShowALoadingScreen", "Loading Screen"),
        new("LaunchScreen.SecondLine", "Loading Screen"),
        new("LaunchScreen.TextSize", "Loading Screen"),
        new("LaunchScreen.TextColour", "Loading Screen"),
        new("LaunchScreen.BackgroundColour", "Loading Screen"),
        new("LaunchScreen.Animation", "Loading Screen"),
        new("LaunchScreen.Speed", "Loading Screen"),
        new("LaunchScreen.Fade", "Loading Screen"),
        new("LaunchScreen.Size", "Loading Screen"),
        new("LaunchScreen.Position", "Loading Screen"),
        new("LaunchScreen.MinTime", "Loading Screen"),
        new("Performance.MaximumFrameRate", "Performance"),
        new("Performance.NoLimit", "Performance"),
        new("Performance.GraphicsAPI", "Performance"),
        new("RobloxSettings.StartWithWindows", "Roblox Settings"),
        new("RobloxSettings.StartHidden", "Roblox Settings"),
        new("RobloxSettings.CloseToTray", "Roblox Settings"),
        new("RobloxSettings.CpuPriority", "Roblox Settings"),
        new("RobloxSettings.Hotkeys", "Roblox Settings"),
        new("RobloxSettings.AutoUpdate", "Roblox Settings"),
        new("RobloxSettings.OpenWebsitePlayButtonsWithNexstrap", "Roblox Settings"),
        new("Integrations.CardIcon", "Integrations"),
        new("Integrations.ShowExperienceNameAndIcon", "Integrations")
    ];

    public static IReadOnlyList<SettingResult> Search(string? query)
    {
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text)) return [];

        return Entries
            .Select(e => new SettingResult(Loc.Get(e.LabelKey), Loc.GetOr("Nav." + e.Page.Replace(" ", ""), e.Page), e.Page))
            .Where(r => r.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase) || r.PageLabel.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(r => r.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .Take(MaxResults)
            .ToList();
    }
}
