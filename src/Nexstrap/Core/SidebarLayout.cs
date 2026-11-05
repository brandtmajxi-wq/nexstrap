using Nexstrap.Models;

namespace Nexstrap.Core;

/// <summary>Works out which pages the sidebar shows and in what order.</summary>
public static class SidebarLayout
{
    public static IReadOnlyList<string> Pages { get; } =
        ["Home", "Games", "Servers", "Appearance", "Loading Screen", "Performance", "FastFlags", "Roblox Settings", "Integrations"];

    /// <summary>Home is where Roblox starts, and Appearance is where hidden pages come back from.</summary>
    public static bool CanHide(string page) => page is not ("Home" or "Appearance");

    /// <summary>The saved order, skipping pages that no longer exist. Pages added in later versions go at the end.</summary>
    public static IReadOnlyList<string> Arrange(SidebarSettings settings) =>
        settings.Order.Where(Pages.Contains).Distinct().Concat(Pages.Where(p => !settings.Order.Contains(p))).ToList();

    public static IReadOnlyList<string> Visible(SidebarSettings settings) =>
        Arrange(settings).Where(p => !settings.Hidden.Contains(p) || !CanHide(p)).ToList();
}
