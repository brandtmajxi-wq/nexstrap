using Nexstrap.Services.Updates;
using Xunit;

namespace Nexstrap.Tests;

public class UpdatePromptTests
{
    private static readonly UpdateCheckResult Newer =
        new(UpdateStatus.UpdateAvailable, "new", new Version(2, 0, 0), Release: new GitHubRelease());

    [Fact]
    public void Offers_a_newer_version_when_the_window_is_on_screen() =>
        Assert.True(UpdatePrompt.ShouldOffer(Newer, alreadyOffered: false, windowVisible: true));

    [Fact]
    public void Only_asks_once_per_run() =>
        Assert.False(UpdatePrompt.ShouldOffer(Newer, alreadyOffered: true, windowVisible: true));

    [Fact]
    public void Waits_while_hidden_in_the_tray() =>
        Assert.False(UpdatePrompt.ShouldOffer(Newer, alreadyOffered: false, windowVisible: false));

    [Theory]
    [InlineData(UpdateStatus.UpToDate)]
    [InlineData(UpdateStatus.NoReleases)] // also what the DEV Launcher reports
    [InlineData(UpdateStatus.Failed)]
    public void Stays_quiet_without_a_newer_version(UpdateStatus status) =>
        Assert.False(UpdatePrompt.ShouldOffer(new UpdateCheckResult(status, "x"), alreadyOffered: false, windowVisible: true));

    [Fact]
    public void Stays_quiet_before_any_check() =>
        Assert.False(UpdatePrompt.ShouldOffer(null, alreadyOffered: false, windowVisible: true));
}
