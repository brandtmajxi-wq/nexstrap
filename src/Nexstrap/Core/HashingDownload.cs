using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Nexstrap.Core;

/// <summary>Streams a URL to disk while hashing it, so the file never has to be read twice.</summary>
public static class HashingDownload
{
    /// <returns>Lower-case hex hash and the number of bytes written.</returns>
    /// <exception cref="InvalidDataException">The response grew past <paramref name="maxBytes"/>.</exception>
    public static async Task<(string Hash, long Bytes)> ToFileAsync(HttpClient http, string url, string path,
        HashAlgorithmName algorithm, long? maxBytes, Action<long>? onBytes, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(algorithm);
        long total = 0;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException("The download is larger than expected.");
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytes?.Invoke(read);
        }

        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }
}
