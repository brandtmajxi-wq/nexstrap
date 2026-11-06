using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class RobloxAutoUpdaterTests
{
    private static RobloxInstallation Build(InstallationSource source) =>
        new(source, @"C:\x\version-old", @"C:\x\version-old\RobloxPlayerBeta.exe", "version-old", "0.1", DateTime.UtcNow);

    private static ClientState Outdated(params RobloxInstallation[] installs) =>
        new(installs, installs.FirstOrDefault(), new ProtocolHandlerInfo(null), new ClientVersionInfo("2", "version-new"), null);

    private sealed class FakeClient(ClientState state) : IRobloxClientService
    {
        public int Installs { get; private set; }
        public Exception? Fails { get; set; }
        public ClientState State { get; } = state;
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsAutomaticSelection => true;
        public Task RefreshAsync(bool checkLatest = true, CancellationToken ct = default) => Task.CompletedTask;
        public void SetPreferred(RobloxInstallation? installation) { }
        public void Remove(RobloxInstallation installation) { }
        public Task<RobloxInstallation> InstallLatestAsync(IProgress<InstallProgress>? progress, CancellationToken ct = default)
        {
            Installs++;
            return Fails is null ? Task.FromResult(State.Installations[0]) : Task.FromException<RobloxInstallation>(Fails);
        }
    }

    private sealed class FakeSession : IRobloxSessionService
    {
        public bool IsActive => false;
        public RobloxActivity? Current => null;
        public DateTime? StartedUtc => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Track(int processId, DateTime launchedAtUtc) { }
        public Task WaitForEndAsync() => Task.CompletedTask;
    }

    private static RobloxAutoUpdater Create(FakeClient client, MemorySettings? settings = null, bool running = false) =>
        new(client, new FakeSession(), settings ?? new MemorySettings(), NullLogger<RobloxAutoUpdater>.Instance, () => running, null);

    [Fact]
    public async Task Updates_a_nexstrap_build_when_roblox_is_closed()
    {
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap)));

        await Create(client).CheckAsync();

        Assert.Equal(1, client.Installs);
    }

    [Fact]
    public async Task Never_updates_while_roblox_is_open()
    {
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap)));

        await Create(client, running: true).CheckAsync();

        Assert.Equal(0, client.Installs);
    }

    [Fact]
    public async Task Respects_the_setting()
    {
        var settings = new MemorySettings();
        settings.Current.Launcher.AutoUpdateRoblox = false;
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap)));

        await Create(client, settings).CheckAsync();

        Assert.Equal(0, client.Installs);
    }

    [Fact]
    public async Task Leaves_a_pinned_build_alone()
    {
        var settings = new MemorySettings();
        settings.Current.Launcher.PreferredInstallation = @"C:\x\version-old";
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap)));

        await Create(client, settings).CheckAsync();

        Assert.Equal(0, client.Installs);
    }

    [Fact]
    public async Task No_surprise_download_for_people_using_another_launchers_build()
    {
        var client = new FakeClient(Outdated(Build(InstallationSource.Voidstrap)));

        await Create(client).CheckAsync();

        Assert.Equal(0, client.Installs);
    }

    [Fact]
    public async Task No_surprise_download_when_roblox_isnt_installed()
    {
        var client = new FakeClient(Outdated());

        await Create(client).CheckAsync();

        Assert.Equal(0, client.Installs);
    }

    [Fact]
    public async Task A_failed_version_isnt_retried_again_and_again()
    {
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap))) { Fails = new InstallException("no") };
        var updater = Create(client);

        await updater.CheckAsync();
        await updater.CheckAsync();

        Assert.Equal(1, client.Installs);
    }

    [Fact]
    public async Task A_paused_update_tries_again_later()
    {
        var client = new FakeClient(Outdated(Build(InstallationSource.Nexstrap))) { Fails = new OperationCanceledException() };
        var updater = Create(client);

        await updater.CheckAsync();
        await updater.CheckAsync();

        Assert.Equal(2, client.Installs);
    }

    [Fact]
    public async Task Up_to_date_means_nothing_happens()
    {
        var build = Build(InstallationSource.Nexstrap);
        var client = new FakeClient(new ClientState([build], build, new ProtocolHandlerInfo(null), new ClientVersionInfo("1", build.VersionHash), null));

        await Create(client).CheckAsync();

        Assert.Equal(0, client.Installs);
    }
}
