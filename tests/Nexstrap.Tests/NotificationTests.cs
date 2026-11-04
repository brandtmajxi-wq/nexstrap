using Nexstrap.Models;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

public class NotificationRulesTests
{
    private static RobloxInstallation Build(InstallationSource source = InstallationSource.Nexstrap, string hash = "version-a") =>
        new(source, @"C:\r", @"C:\r\RobloxPlayerBeta.exe", hash, "0.1", DateTime.Now);

    private static FastFlagEntry Flag(string name, bool enabled = true) => new() { Name = name, Value = "1", Enabled = enabled };

    private static IReadOnlyList<AppNotification> Evaluate(Version? update = null, RobloxInstallation? build = null, ClientVersionInfo? latest = null,
        int? cap = null, int? hz = null, params FastFlagEntry[] flags) =>
        NotificationRules.Evaluate(new NotificationInputs(update, build, latest, flags, cap, hz));

    [Fact]
    public void Healthy_setup_has_no_notifications() =>
        Assert.Empty(Evaluate(build: Build(), latest: new ClientVersionInfo("1", "version-a"), cap: 60, hz: 60,
            flags: Flag("FIntDebugForceMSAASamples")));

    [Fact]
    public void Update_keys_include_the_version_so_new_ones_reappear()
    {
        Assert.Equal("nexstrap-update:1.5.0", Assert.Single(Evaluate(update: new Version(1, 5, 0))).Key);

        var roblox = Assert.Single(Evaluate(build: Build(), latest: new ClientVersionInfo("2.0", "version-b")));
        Assert.Equal("roblox-update:version-b", roblox.Key);
        Assert.Equal(NotificationCategory.RobloxUpdate, roblox.Category);
    }

    [Fact]
    public void Warns_about_ignored_flags_but_not_disabled_ones()
    {
        var note = Assert.Single(Evaluate(build: Build(), flags: [Flag("DFIntTaskSchedulerTargetFps"), Flag("FFlagFoo", enabled: false)]));
        Assert.Contains("1 FastFlag", note.Title);
        Assert.Equal(NotificationTarget.FastFlags, note.Target);
    }

    [Fact]
    public void Warns_when_flags_cannot_reach_the_build_in_use()
    {
        var note = Assert.Single(Evaluate(build: Build(InstallationSource.Voidstrap), flags: Flag("FIntDebugForceMSAASamples")));
        Assert.Contains("aren't being applied", note.Title);
        Assert.Empty(Evaluate(build: Build(InstallationSource.Voidstrap)));
    }

    [Fact]
    public void Warns_when_cap_is_above_the_display()
    {
        Assert.Equal(NotificationTarget.Performance, Assert.Single(Evaluate(cap: 240, hz: 60)).Target);
        Assert.Empty(Evaluate(cap: 60, hz: 59));
        Assert.Empty(Evaluate(cap: 240));
    }

    [Fact]
    public void Preferences_switch_categories_off()
    {
        var options = new NotificationSettings { RobloxUpdates = false };
        Assert.False(NotificationRules.IsEnabled(NotificationCategory.RobloxUpdate, options));
        Assert.True(NotificationRules.IsEnabled(NotificationCategory.NexstrapUpdate, options));
        Assert.True(NotificationRules.IsEnabled(NotificationCategory.Configuration, options));
    }
}
