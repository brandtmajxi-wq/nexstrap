using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexstrap.Services.Discord;
using Xunit;

namespace Nexstrap.Tests;

public class CardImageUploadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nexstrap-card").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static BitmapSource Pixels() =>
        BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);

    private string Save(string name, BitmapEncoder encoder)
    {
        var path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private string Png(string name = "my private photo.png")
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Pixels()));
        return Save(name, encoder);
    }

    private sealed class FakeHost(HttpStatusCode status, string reply) : HttpMessageHandler
    {
        public string? Sent { get; private set; }

        public Uri? Url { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Url = request.RequestUri;
            Sent = Encoding.Latin1.GetString(await request.Content!.ReadAsByteArrayAsync(cancel));
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }
    }

    [Fact]
    public async Task Upload_returns_the_hosts_link_and_hides_the_file_name()
    {
        var host = new FakeHost(HttpStatusCode.OK, "https://files.catbox.moe/abc123.png\n");

        var link = await new CardImageUploader(new HttpClient(host)).UploadAsync(Png());

        Assert.Equal("https://files.catbox.moe/abc123.png", link);
        Assert.Equal("catbox.moe", host.Url!.Host);
        Assert.Contains("fileupload", host.Sent);
        Assert.Contains("image.png", host.Sent);
        Assert.DoesNotContain("private", host.Sent);
    }

    [Fact]
    public async Task A_failed_upload_is_reported()
    {
        var uploader = new CardImageUploader(new HttpClient(new FakeHost(HttpStatusCode.InternalServerError, "nope")));

        await Assert.ThrowsAsync<HttpRequestException>(() => uploader.UploadAsync(Png()));
    }

    [Theory]
    [InlineData("Internal server error")]
    [InlineData("")]
    [InlineData("http://files.catbox.moe/abc.png")]
    [InlineData("https://example.com/abc.png")]
    [InlineData("https://files.catbox.moe.example.com/abc.png")]
    public void Only_a_link_on_the_hosts_file_domain_is_accepted(string reply) =>
        Assert.Throws<InvalidDataException>(() => CardImageUploader.ReadLink(reply));

    [Fact]
    public void Jpg_metadata_is_removed_before_upload()
    {
        var metadata = new BitmapMetadata("jpg") { Comment = "taken-at-my-house" };
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Pixels(), null, metadata, null));
        var path = Save("photo.jpg", encoder);
        Assert.Contains("taken-at-my-house", Encoding.Latin1.GetString(File.ReadAllBytes(path)));

        var (bytes, name) = CardImageUploader.Prepare(path);

        Assert.Equal("image.jpg", name);
        Assert.DoesNotContain("taken-at-my-house", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void Other_file_types_are_refused()
    {
        var path = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(path, "hello");

        Assert.Throws<InvalidDataException>(() => CardImageUploader.Prepare(path));
    }

    [Fact]
    public void A_renamed_file_that_isnt_an_image_is_refused()
    {
        var path = Path.Combine(_dir, "fake.png");
        File.WriteAllText(path, "not really an image");

        Assert.Throws<InvalidDataException>(() => CardImageUploader.Prepare(path));
    }

    [Fact]
    public void Files_over_the_limit_are_refused()
    {
        var path = Path.Combine(_dir, "big.png");
        using (var file = File.Create(path)) file.SetLength(CardImageUploader.MaxMegabytes * 1024L * 1024L + 1);

        Assert.Throws<InvalidDataException>(() => CardImageUploader.Prepare(path));
    }
}
