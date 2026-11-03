using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Updates;

namespace Nexstrap.Tests;

public class RobloxLocatorTests
{
    [Fact]
    public void Finds_only_folders_containing_the_player_newest_first()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Versions");
        Directory.CreateDirectory(Path.Combine(root, "version-studioonly"));
        File.WriteAllText(Path.Combine(root, "version-studioonly", "RobloxStudioBeta.exe"), "");

        var older = Path.Combine(root, "version-aaaa");
        var newer = Path.Combine(root, "version-bbbb");
        foreach (var d in new[] { older, newer })
        {
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, RobloxLocator.PlayerExecutable), "");
        }
        File.SetLastWriteTime(Path.Combine(older, RobloxLocator.PlayerExecutable), DateTime.Now.AddDays(-3));

        var found = new RobloxLocator(NullLogger<RobloxLocator>.Instance)
            .Scan([(InstallationSource.Voidstrap, root), (InstallationSource.Official, Path.Combine(dir.Path, "missing"))]);

        Assert.Equal(["version-bbbb", "version-aaaa"], found.Select(f => f.VersionHash));
        Assert.All(found, f => Assert.Equal(InstallationSource.Voidstrap, f.Source));
    }

    [Theory]
    [InlineData("0, 745, 0, 7451137", "0.745.0.7451137")]
    [InlineData("0.745.0.7451137", "0.745.0.7451137")]
    [InlineData("0, 0, 0, 0", null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void Normalises_file_versions(string raw, string? expected) =>
        Assert.Equal(expected, RobloxLocator.NormaliseFileVersion(raw));

    [Theory]
    [InlineData("\"C:\\Users\\x\\AppData\\Local\\Voidstrap\\Voidstrap.exe\" -player \"%1\"", "Voidstrap")]
    [InlineData("\"C:\\Users\\x\\AppData\\Local\\Roblox\\Versions\\version-1\\RobloxPlayerBeta.exe\" %1", "Roblox")]
    [InlineData(null, "Nobody")]
    public void Identifies_protocol_owner(string? command, string owner) =>
        Assert.Equal(owner, new ProtocolHandlerInfo(command).OwnerName);
}

public class UpdateServiceTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static UpdateService Create(HttpStatusCode status, string body = "{}") =>
        new(new HttpClient(new StubHandler(status, body)), NullLogger<UpdateService>.Instance);

    [Fact]
    public async Task Missing_repository_reports_no_releases()
    {
        var result = await Create(HttpStatusCode.NotFound).CheckAsync();
        Assert.Equal(UpdateStatus.NoReleases, result.Status);
    }

    [Fact]
    public async Task Newer_tag_reports_update_available()
    {
        var result = await Create(HttpStatusCode.OK,
            """{ "tag_name": "v99.0.0", "body": "notes", "html_url": "https://github.com/x/y/releases/v99.0.0", "assets": [] }""").CheckAsync();

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(99, 0, 0), result.LatestVersion);
        Assert.Equal("notes", result.ReleaseNotes);
    }

    [Fact]
    public async Task Same_or_older_tag_reports_up_to_date()
    {
        var result = await Create(HttpStatusCode.OK, """{ "tag_name": "v0.0.1" }""").CheckAsync();
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Network_failure_is_reported_not_thrown()
    {
        var result = await Create(HttpStatusCode.InternalServerError).CheckAsync();
        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v2.0.0-beta.1", 2, 0, 0)]
    public void Parses_tags(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }
}
