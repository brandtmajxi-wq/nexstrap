using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

/// <summary>
/// Talks to Roblox's real deployment CDN (~225 MB). Opt-in only:
///   set NEXSTRAP_LIVE_TESTS=1 before running dotnet test.
/// Everything is written to a temp folder and deleted afterwards.
/// </summary>
public class LiveRobloxTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("NEXSTRAP_LIVE_TESTS") == "1";

    [Fact]
    public async Task Installs_the_current_live_build()
    {
        if (!Enabled) return;

        using var dir = new TempDir();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var deployment = new RobloxDeployment(http, NullLogger<RobloxDeployment>.Instance, dir.File("Versions"), dir.File("Downloads"));

        var latest = await deployment.GetLatestVersionAsync();
        var installDir = await deployment.InstallAsync(latest, null);

        var found = new RobloxLocator(NullLogger<RobloxLocator>.Instance).Scan([(InstallationSource.Nexstrap, dir.File("Versions"))]);
        var install = Assert.Single(found);
        Assert.Equal(latest.VersionHash, install.VersionHash);
        Assert.Equal(latest.Version, install.ClientVersion);
        Assert.True(Directory.Exists(Path.Combine(installDir, "content", "fonts")));
        Assert.True(Directory.Exists(Path.Combine(installDir, "PlatformContent", "pc", "textures")));
        Assert.True(Directory.Exists(Path.Combine(installDir, "shaders")));

        Console.WriteLine($"LIVE: installed {latest.Version} ({latest.VersionHash}), " +
                          $"{Directory.GetFiles(installDir, "*", SearchOption.AllDirectories).Length} files");
    }

    /// <summary>About 600 MB. Also compares the folder layout with Roblox's own Studio install, if there is one.</summary>
    [Fact]
    public async Task Installs_the_current_live_studio()
    {
        if (!Enabled) return;

        using var dir = new TempDir();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var deployment = new RobloxDeployment(http, NullLogger<RobloxDeployment>.Instance, dir.File("Studio"), dir.File("Downloads"), RobloxProduct.Studio);

        var latest = await deployment.GetLatestVersionAsync();
        var installDir = await deployment.InstallAsync(latest, null);

        var build = Assert.Single(StudioLocator.Scan([(InstallationSource.Nexstrap, dir.File("Studio"))]));
        Assert.Equal(latest.Version, build.Version);

        var ours = Directory.GetDirectories(installDir, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(installDir, d)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var official = StudioLocator.Scan([(InstallationSource.Official, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"))]).FirstOrDefault();
        if (official is not null)
        {
            var theirs = new[] { "content", "ExtraContent", "PlatformContent", "StudioContent", "BuiltInPlugins", "Plugins", "StudioFonts", "ApplicationConfig" }
                .SelectMany(top => Directory.Exists(Path.Combine(official.Directory, top))
                    ? Directory.GetDirectories(Path.Combine(official.Directory, top)).Select(d => Path.GetRelativePath(official.Directory, d))
                    : [])
                .ToList();
            var missing = theirs.Where(d => !ours.Contains(d)).ToList();
            Console.WriteLine("LIVE: folders in Roblox's Studio missing from ours: " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
            Assert.Empty(missing);
        }

        Console.WriteLine($"LIVE: installed Studio {latest.Version}, {Directory.GetFiles(installDir, "*", SearchOption.AllDirectories).Length} files");
    }
}
