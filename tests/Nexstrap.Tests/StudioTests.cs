using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class StudioTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Build(string root, string hash, bool complete, int daysOld = 0)
    {
        var dir = Path.Combine(_dir.File(root), hash);
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, StudioLocator.StudioExecutable);
        File.WriteAllText(exe, "");
        File.SetLastWriteTime(exe, DateTime.Now.AddDays(-daysOld));
        if (complete) File.WriteAllText(Path.Combine(dir, RobloxDeployment.CompleteMarker), "");
        return dir;
    }

    [Fact]
    public void Finds_studio_but_not_player_builds_or_unfinished_nexstrap_ones()
    {
        Build("official", "version-aaa", complete: false);
        Directory.CreateDirectory(Path.Combine(_dir.File("official"), "version-player"));
        File.WriteAllText(Path.Combine(_dir.File("official"), "version-player", RobloxLocator.PlayerExecutable), "");
        Build("nexstrap", "version-half", complete: false);

        var builds = StudioLocator.Scan([(InstallationSource.Nexstrap, _dir.File("nexstrap")), (InstallationSource.Official, _dir.File("official"))]);

        var only = Assert.Single(builds);
        Assert.Equal("version-aaa", only.VersionHash);
    }

    [Fact]
    public void Prefers_nexstraps_own_copy_even_if_older()
    {
        Build("official", "version-new", complete: false, daysOld: 0);
        Build("nexstrap", "version-old", complete: true, daysOld: 5);

        var builds = StudioLocator.Scan([(InstallationSource.Nexstrap, _dir.File("nexstrap")), (InstallationSource.Official, _dir.File("official"))]);

        Assert.Equal("version-old", StudioLocator.Pick(builds)!.VersionHash);
    }

    [Fact]
    public void The_nexstrap_studio_folder_isnt_the_player_folder() =>
        Assert.NotEqual(Nexstrap.Core.AppInfo.Paths.Versions, StudioLocator.SearchRoots().First(r => r.Source == InstallationSource.Nexstrap).Root);

    private sealed class FakeDeployment(string latest) : IRobloxDeployment
    {
        public Task<ClientVersionInfo> GetLatestVersionAsync(CancellationToken ct = default) => Task.FromResult(new ClientVersionInfo("0.746", latest));
        public Task<string> InstallAsync(ClientVersionInfo version, IProgress<InstallProgress>? progress, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> RepairAsync(ClientVersionInfo version, IProgress<InstallProgress>? progress, CancellationToken ct = default) => Task.FromResult("");
        public string DirectoryFor(string versionHash) => versionHash;
    }

    [Fact]
    public async Task Reports_an_update_when_the_build_is_behind()
    {
        Build("official", "version-old", complete: false);
        var service = new StudioService(new FakeDeployment("version-new"), NullLogger<StudioService>.Instance,
            () => StudioLocator.Scan([(InstallationSource.Official, _dir.File("official"))]));

        await service.RefreshAsync();

        Assert.True(service.UpdateAvailable);
    }

    [Fact]
    public async Task No_update_and_no_crash_without_studio()
    {
        var service = new StudioService(new FakeDeployment("version-new"), NullLogger<StudioService>.Instance, () => []);

        await service.RefreshAsync();

        Assert.False(service.UpdateAvailable);
        Assert.False(service.Open().Success);
    }

    [Fact]
    public void Studio_packages_have_distinct_known_places()
    {
        var map = RobloxDeployment.StudioPackageDirectories;
        Assert.Equal("", map["RobloxStudio.zip"]);
        Assert.Equal(@"StudioContent\textures", map["studiocontent-textures.zip"]);
        Assert.Equal(@"ExtraContent\scripts", map["extracontent-scripts.zip"]);
        Assert.Equal("WindowsStudio64", RobloxProduct.Studio.BinaryType);
        Assert.Equal(RobloxDeployment.VersionEndpoint, RobloxProduct.Player.VersionEndpoint);
    }
}
