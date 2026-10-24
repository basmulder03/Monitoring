using System.IO.Compression;
using System.Text.Json;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>
/// Downloads advisories from a public source. It only ever asks for the address it is given (and the pages that source names), with a plain GET: nothing about the
/// packages in use is in a request. Everything that comes back is treated as untrusted: the size is bounded before and after decompression and the content is only parsed.
/// </summary>
/// <param name="http">Sends the requests.</param>
/// <param name="options">Provides the size limit.</param>
internal sealed class AdvisoryFetcher(HttpClient http, VulnerabilitiesOptions options)
{
    private const int MaxZipEntries = 100_000;
    private const long MaxZipEntryBytes = 2 * 1024 * 1024;

    /// <summary>Downloads and reads one source.</summary>
    /// <param name="source">The source.</param>
    /// <param name="ct">Cancels the download.</param>
    /// <returns>Its advisories about NuGet packages.</returns>
    /// <exception cref="HttpRequestException">The source cannot be reached or answers with an error.</exception>
    /// <exception cref="InvalidDataException">The source sends more than allowed, or something that is not what it should be.</exception>
    /// <exception cref="JsonException">The content is not JSON of the expected shape.</exception>
    public async Task<IReadOnlyList<OsvAdvisory>> FetchAsync(AdvisorySource source, CancellationToken ct) =>
        source.Kind switch
        {
            AdvisorySource.NuGetKind => await FetchNuGetAsync(source.Url, ct).ConfigureAwait(false),
            AdvisorySource.OsvKind => ReadOsv(await DownloadAsync(source.Url, ct).ConfigureAwait(false)),
            _ => throw new InvalidDataException($"The kind of source '{source.Kind}' is not known."),
        };

    private async Task<IReadOnlyList<OsvAdvisory>> FetchNuGetAsync(Uri index, CancellationToken ct)
    {
        var listed = JsonSerializer.Deserialize(await DownloadAsync(index, ct).ConfigureAwait(false), NuGetFeedJsonContext.Default.ListNuGetPage)
            ?? throw new InvalidDataException("The vulnerability index of the source is empty.");

        // The pages must be on the same host as the index, so a source cannot send the controller somewhere else.
        var pages = new List<Dictionary<string, List<NuGetEntry>>>();
        foreach (var name in new[] { "base", "update" })
        {
            foreach (var page in listed.Where(p => p.Name == name && p.Id is not null))
            {
                var address = new Uri(page.Id!, UriKind.Absolute);
                if (!address.Host.EndsWith(RegistrableHost(index.Host), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The page '{address.Host}' is not on the host of the source.");
                }

                pages.Add(JsonSerializer.Deserialize(await DownloadAsync(address, ct).ConfigureAwait(false), NuGetFeedJsonContext.Default.DictionaryStringListNuGetEntry)
                    ?? throw new InvalidDataException("A page of the vulnerability feed is empty."));
            }
        }

        return NuGetFeed.ToAdvisories(pages);
    }

    /// <summary>The last two labels of a host name, so <c>api.nuget.org</c> and <c>azuresearch.nuget.org</c> belong together.</summary>
    private static string RegistrableHost(string host)
    {
        var labels = host.Split('.');
        return labels.Length <= 2 ? host : "." + string.Join('.', labels[^2..]);
    }

    private static IReadOnlyList<OsvAdvisory> ReadOsv(byte[] bytes)
    {
        var advisories = new List<OsvAdvisory>();
        if (bytes is [(byte)'P', (byte)'K', ..])
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries)
            {
                throw new InvalidDataException("The zip holds more files than allowed.");
            }

            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                // The size is what the zip says; the read below stops at the limit whatever it says.
                if (entry.Length > MaxZipEntryBytes)
                {
                    continue;
                }

                using var stream = entry.Open();
                using var limited = new MemoryStream();
                CopyLimited(stream, limited, MaxZipEntryBytes);
                advisories.AddRange(AdvisoryStore.Parse(System.Text.Encoding.UTF8.GetString(limited.GetBuffer(), 0, (int)limited.Length)));
            }
        }
        else
        {
            advisories.AddRange(AdvisoryStore.Parse(System.Text.Encoding.UTF8.GetString(bytes)));
        }

        // Only advisories about NuGet packages are of use, and keeping only them keeps what is held small.
        return [.. advisories
            .Select(a => a with { Affected = a.Affected?.Where(x => string.Equals(x.Package?.Ecosystem, "NuGet", StringComparison.OrdinalIgnoreCase)).ToList() })
            .Where(a => a.Affected is { Count: > 0 })];
    }

    private async Task<byte[]> DownloadAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Advisories are only fetched over HTTPS.");
        }

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The source redirected to an address that is not HTTPS.");
        }

        if (response.Content.Headers.ContentLength > options.MaxDownloadBytes)
        {
            throw new InvalidDataException($"The source sends more than {options.MaxDownloadBytes} bytes.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(body, buffer, options.MaxDownloadBytes, ct).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        if (bytes is [0x1f, 0x8b, ..])
        {
            using var plain = new MemoryStream();
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            CopyLimited(gzip, plain, options.MaxDownloadBytes);
            return plain.ToArray();
        }

        return bytes;
    }

    private static async Task CopyLimitedAsync(Stream from, Stream to, long limit, CancellationToken ct)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = await from.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (to.Length + read > limit)
            {
                throw new InvalidDataException($"The source sends more than {limit} bytes.");
            }

            to.Write(chunk, 0, read);
        }
    }

    private static void CopyLimited(Stream from, Stream to, long limit)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = from.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (to.Length + read > limit)
            {
                throw new InvalidDataException($"More than {limit} bytes after decompression.");
            }

            to.Write(chunk, 0, read);
        }
    }
}
