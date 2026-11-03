using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Services.Updates;

namespace Nexstrap.Tests;

/// <summary>
/// Feeds a real release package (tools/package-release.ps1 output) through the updater's
/// verification, exactly as GitHub would serve it. Opt-in: set NEXSTRAP_PACKAGE_DIR to the folder.
/// </summary>
public class ReleasePackageTests
{
    [Fact]
    public async Task Packaged_release_passes_the_updaters_checks()
    {
        var dir = Environment.GetEnvironmentVariable("NEXSTRAP_PACKAGE_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        var exe = await File.ReadAllBytesAsync(Path.Combine(dir, "Nexstrap.exe"));
        var sums = await File.ReadAllBytesAsync(Path.Combine(dir, "SHA256SUMS.txt"));
        var tag = "v" + AppInfo.VersionString;
        var baseUrl = $"https://github.com/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases/download/{tag}/";

        var handler = new Served(new() { [baseUrl + "Nexstrap.exe"] = exe, [baseUrl + "SHA256SUMS.txt"] = sums });
        using var temp = new TempDir();
        var installer = new UpdateInstaller(new HttpClient(handler), NullLogger<UpdateInstaller>.Instance, temp.Path,
            AppInfo.RepositoryOwner, AppInfo.RepositoryName);

        var release = new GitHubRelease { TagName = tag };
        release.Assets.Add(new GitHubAsset { Name = "Nexstrap.exe", Size = exe.Length, DownloadUrl = baseUrl + "Nexstrap.exe" });
        release.Assets.Add(new GitHubAsset { Name = "SHA256SUMS.txt", Size = sums.Length, DownloadUrl = baseUrl + "SHA256SUMS.txt" });

        var verified = await installer.DownloadAndVerifyAsync(release, null);
        Console.WriteLine($"PACKAGE OK: {verified.Version} {verified.Sha256}");
        Assert.Equal(AppInfo.VersionString, verified.Version.ToString(3));
    }

    private sealed class Served(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
