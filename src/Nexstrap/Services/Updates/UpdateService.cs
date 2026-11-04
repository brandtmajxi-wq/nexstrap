using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Updates;

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    NoReleases,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string Message,
    Version? LatestVersion = null,
    string? ReleaseNotes = null,
    string? ReleaseUrl = null,
    GitHubRelease? Release = null);

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";

    /// <summary>GitHub's own checksum, e.g. "sha256:ab12…" (present for assets uploaded since mid-2025).</summary>
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

public interface IUpdateService
{
    UpdateCheckResult? LastResult { get; }

    /// <summary>Raised after every check (startup or manual). May be raised on a background thread.</summary>
    event EventHandler<UpdateCheckResult>? Checked;

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks GitHub for the latest published release of Nexstrap and compares it with this build.
/// Read-only: downloading and installing is handled separately (and only after verification).
/// </summary>
public sealed class UpdateService(HttpClient http, ILogger<UpdateService> logger) : IUpdateService
{
    public UpdateCheckResult? LastResult { get; private set; }

    public event EventHandler<UpdateCheckResult>? Checked;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases/latest";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.VersionString));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Remember(new UpdateCheckResult(UpdateStatus.NoReleases, Loc.Get("Update.NoNexstrapReleasesHaveBeenPublished")));
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Remember(new UpdateCheckResult(UpdateStatus.Failed, Loc.Get("Update.GitHubIsRateLimitingUpdateChecks")));
            response.EnsureSuccessStatusCode();

            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (release is null || !TryParseVersion(release.TagName, out var latest))
                return Remember(new UpdateCheckResult(UpdateStatus.Failed, Loc.Get("Update.TheLatestReleaseHasAnUnrecognised")));

            var current = new Version(AppInfo.Version.Major, AppInfo.Version.Minor, AppInfo.Version.Build);
            var result = latest > current
                ? new UpdateCheckResult(UpdateStatus.UpdateAvailable, Loc.Format("Update.NexstrapIsAvailable", latest), latest, release.Body, release.HtmlUrl, release)
                : new UpdateCheckResult(UpdateStatus.UpToDate, Loc.Get("Update.YoureOnTheLatestVersion"), latest, release.Body, release.HtmlUrl, release);
            logger.LogInformation("Update check: current {Current}, latest {Latest}", current, latest);
            return Remember(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Remember(new UpdateCheckResult(UpdateStatus.Failed, Loc.Get("Update.TheUpdateCheckTookTooLong")));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Update check failed");
            return Remember(new UpdateCheckResult(UpdateStatus.Failed, Loc.Get("Update.CouldntReachGitHubCheckYourConnection")));
        }
    }

    /// <summary>Accepts tags like "v1.2.3", "1.2.3" or "v1.2".</summary>
    internal static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0) text = text[..dash];
        if (Version.TryParse(text, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private UpdateCheckResult Remember(UpdateCheckResult result)
    {
        LastResult = result;
        Checked?.Invoke(this, result);
        return result;
    }
}
