using Nexstrap.Core;
using Nexstrap.Models;
using Xunit;

namespace Nexstrap.Tests;

public class SidebarTests
{
    [Fact]
    public void Default_sidebar_shows_every_page_in_order()
    {
        Assert.Equal(SidebarLayout.Pages, SidebarLayout.Visible(new SidebarSettings()));
    }

    [Fact]
    public void Saved_order_is_used_and_hidden_pages_are_left_out()
    {
        var settings = new SidebarSettings { Order = ["Games", "Home"], Hidden = ["Servers"] };

        var visible = SidebarLayout.Visible(settings);

        Assert.Equal(["Games", "Home"], visible.Take(2));
        Assert.DoesNotContain("Servers", visible);
        Assert.Equal(SidebarLayout.Pages.Count - 1, visible.Count);
    }

    [Fact]
    public void Home_and_Appearance_can_never_be_hidden()
    {
        var visible = SidebarLayout.Visible(new SidebarSettings { Hidden = ["Home", "Appearance", "Games"] });

        Assert.Contains("Home", visible);
        Assert.Contains("Appearance", visible);
        Assert.DoesNotContain("Games", visible);
    }

    [Fact]
    public void Unknown_and_repeated_names_in_the_file_are_ignored()
    {
        var arranged = SidebarLayout.Arrange(new SidebarSettings { Order = ["Old page", "Games", "Games"] });

        Assert.Equal(SidebarLayout.Pages.Count, arranged.Count);
        Assert.Equal("Games", arranged[0]);
        Assert.DoesNotContain("Old page", arranged);
    }

    [Fact]
    public void Pages_added_later_appear_at_the_end()
    {
        var saved = SidebarLayout.Pages.Take(3).Reverse().ToList();

        var arranged = SidebarLayout.Arrange(new SidebarSettings { Order = saved });

        Assert.Equal(saved, arranged.Take(3));
        Assert.Equal(SidebarLayout.Pages[^1], arranged[^1]);
    }
}
