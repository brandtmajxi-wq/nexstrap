using System.IO;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class RepairTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexstrap-swap-" + Guid.NewGuid().ToString("N"));

    public RepairTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Folder(string name, string file, string text)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), text);
        return dir;
    }

    [Fact]
    public void A_new_build_replaces_the_old_one_and_nothing_is_left_over()
    {
        var target = Folder("version-abc", "RobloxPlayerBeta.exe", "old");
        var staging = Folder("_staging-version-abc", "RobloxPlayerBeta.exe", "new");

        RobloxDeployment.SwapIn(staging, target);

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "RobloxPlayerBeta.exe")));
        Assert.False(Directory.Exists(staging));
        Assert.Equal(["version-abc"], Directory.GetDirectories(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_first_install_just_moves_in()
    {
        var staging = Folder("_staging-version-new", "RobloxPlayerBeta.exe", "new");
        var target = Path.Combine(_root, "version-new");
        RobloxDeployment.SwapIn(staging, target);
        Assert.True(File.Exists(Path.Combine(target, "RobloxPlayerBeta.exe")));
    }

    [Fact]
    public void If_the_old_build_is_in_use_it_stays_exactly_as_it_was()
    {
        var target = Folder("version-abc", "RobloxPlayerBeta.exe", "old");
        var staging = Folder("_staging-version-abc", "RobloxPlayerBeta.exe", "new");

        // An open file stops Windows from renaming its folder, like a running Roblox does.
        using (File.Open(Path.Combine(target, "RobloxPlayerBeta.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => RobloxDeployment.SwapIn(staging, target));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "RobloxPlayerBeta.exe")));
    }
}
