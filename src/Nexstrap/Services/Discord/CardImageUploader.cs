using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using Nexstrap.Core;

namespace Nexstrap.Services.Discord;

public interface ICardImageUploader
{
    /// <summary>Uploads an image and returns its public https link.</summary>
    /// <exception cref="InvalidDataException">The file can't be used, or the host's reply had no usable link.</exception>
    Task<string> UploadAsync(string path, CancellationToken cancel = default);
}

/// <summary>
/// Discord only shows card images from a public link, so a file on this PC has to go to an image host first.
/// Callers must get the user's agreement before calling this: anonymous uploads are public and can't be deleted.
/// </summary>
public sealed class CardImageUploader(HttpClient http) : ICardImageUploader
{
    internal const int MaxMegabytes = 10;

    private const string Endpoint = "https://catbox.moe/user/api.php";
    private const string ImageHost = "files.catbox.moe";

    public async Task<string> UploadAsync(string path, CancellationToken cancel = default)
    {
        var (bytes, name) = Prepare(path);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");
        form.Add(new ByteArrayContent(bytes), "fileToUpload", name);

        using var response = await http.PostAsync(Endpoint, form, cancel);
        response.EnsureSuccessStatusCode();
        return ReadLink(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>
    /// Checks the file really is an image. The upload gets a neutral name, and JPGs are saved again
    /// without their metadata, because photos can carry the place they were taken.
    /// </summary>
    internal static (byte[] Bytes, string Name) Prepare(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
            throw new InvalidDataException(Loc.Get("Integrations.CardIcon.NotAnImage"));
        if (new FileInfo(path).Length > MaxMegabytes * 1024L * 1024L)
            throw new InvalidDataException(Loc.Format("Integrations.CardIcon.TooLarge", MaxMegabytes));

        var bytes = File.ReadAllBytes(path);
        try
        {
            using var source = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (extension is ".jpg" or ".jpeg")
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0], null, null, null));
                using var clean = new MemoryStream();
                encoder.Save(clean);
                bytes = clean.ToArray();
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(Loc.Get("Integrations.CardIcon.NotAnImage"), ex);
        }

        return (bytes, "image" + extension);
    }

    /// <summary>Only a link on the host's own file domain is accepted; anything else is an error message.</summary>
    internal static string ReadLink(string reply)
    {
        var link = reply.Trim();
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host == ImageHost && RichPresenceService.IsValidImage(link))
            return link;
        throw new InvalidDataException(Loc.Get("Integrations.CardIcon.BadReply"));
    }
}
