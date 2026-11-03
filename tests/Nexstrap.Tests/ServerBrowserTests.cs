using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Services.Roblox;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.Tests;

public class ServerBrowserTests
{
    private const string Id1 = "b6c72548-3e72-4029-a7b6-056e397f1d18", Id2 = "e56b49fa-50d5-4017-a51d-aeb439707eaf";

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (ServerBrowserService, Stub) Create(HttpStatusCode status, string body = "{}")
    {
        var stub = new Stub(status, body);
        return (new ServerBrowserService(new HttpClient(stub), NullLogger<ServerBrowserService>.Instance), stub);
    }

    [Fact]
    public async Task Parses_servers_and_paging()
    {
        var (service, stub) = Create(HttpStatusCode.OK, $$"""
            { "nextPageCursor": "abc+1", "data": [
              { "id": "{{Id1}}", "maxPlayers": 15, "playing": 9, "fps": 59.8, "ping": 86, "playerTokens": [], "players": [] },
              { "id": "{{Id2}}", "maxPlayers": 15, "playing": 15, "fps": 60 },
              { "id": "not-a-guid", "maxPlayers": 15, "playing": 1, "fps": 60 } ] }
            """);

        var page = await service.GetServersAsync(1818, mostPlayersFirst: false, excludeFull: true, cursor: "x y");

        Assert.Equal(2, page.Servers.Count);
        Assert.Equal(new GameServer(Id1, 9, 15, 59.8, 86), page.Servers[0]);
        Assert.True(page.Servers[1].IsFull);
        Assert.Null(page.Servers[1].Ping);
        Assert.Equal("abc+1", page.NextCursor);
        Assert.Contains("/1818/servers/Public", stub.Url);
        Assert.Contains("sortOrder=Asc", stub.Url);
        Assert.Contains("excludeFullGames=true", stub.Url);
        Assert.Contains("cursor=x%20y", stub.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "limiting")]
    [InlineData(HttpStatusCode.BadRequest, "place ID")]
    [InlineData(HttpStatusCode.InternalServerError, "Couldn't reach")]
    public async Task Failures_become_friendly_messages(HttpStatusCode status, string expected)
    {
        var (service, _) = Create(status);
        var ex = await Assert.ThrowsAsync<ServerBrowserException>(() => service.GetServersAsync(1, true, false));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData(9, 15, 86, true, 0, 0, true)]
    [InlineData(15, 15, 86, true, 0, 0, false)]   // full, hidden
    [InlineData(15, 15, 86, false, 0, 0, true)]   // full, shown
    [InlineData(13, 15, 86, true, 3, 0, false)]   // only 2 free slots
    [InlineData(9, 15, 86, true, 0, 50, false)]   // ping too high
    [InlineData(9, 15, 40, true, 0, 50, true)]
    public void Filters_servers(int playing, int max, int ping, bool hideFull, int minFree, int maxPing, bool visible) =>
        Assert.Equal(visible, ServersViewModel.Matches(new GameServer(Id1, playing, max, 60, ping), hideFull, minFree, maxPing));

    [Fact]
    public void Servers_without_ping_fail_a_ping_limit() =>
        Assert.False(ServersViewModel.Matches(new GameServer(Id1, 1, 15, 60, null), false, 0, 100));
}
