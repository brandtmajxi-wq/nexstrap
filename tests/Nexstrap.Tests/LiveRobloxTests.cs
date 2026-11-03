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
}
