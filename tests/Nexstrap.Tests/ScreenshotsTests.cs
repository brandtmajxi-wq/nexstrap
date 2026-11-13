using System.IO;
using Nexstrap.ViewModels.Pages;
using Xunit;

namespace Nexstrap.Tests;

public class ScreenshotsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexstrap-shots-" + Guid.NewGuid().ToString("N"));

    public ScreenshotsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private void Make(string name, int minutesAgo)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    [Fact]
    public void Only_pictures_newest_first()
    {
        Make("old.png", 60);
        Make("new.JPG", 1);
        Make("middle.jpeg", 30);
        Make("notes.txt", 0);
        Directory.CreateDirectory(Path.Combine(_folder, "sub.png"));

        Assert.Equal(["new.JPG", "middle.jpeg", "old.png"], ScreenshotsViewModel.Find(_folder, 10).Select(f => f.Name));
    }

    [Fact]
    public void A_big_folder_is_capped()
    {
        for (var i = 0; i < 12; i++) Make($"shot{i}.png", i);
        var found = ScreenshotsViewModel.Find(_folder, 5);
        Assert.Equal(5, found.Count);
        Assert.Equal("shot0.png", found[0].Name);
    }

    [Fact]
    public void A_missing_folder_is_just_empty() =>
        Assert.Empty(ScreenshotsViewModel.Find(Path.Combine(_folder, "nope"), 10));
}
