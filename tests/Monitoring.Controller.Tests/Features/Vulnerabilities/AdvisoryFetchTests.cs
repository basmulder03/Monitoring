using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Packages;
using Monitoring.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Vulnerabilities;

public class AdvisoryFetchTests
{
    private static readonly Uri NuGetIndex = new("https://api.nuget.org/v3/vulnerabilities/index.json");
    private static readonly Uri OsvZip = new("https://osv.example.org/NuGet/all.zip");

    private const string Index = """
        [ { "@name": "base", "@id": "https://api.nuget.org/v3-vulnerabilities/1/base.json" },
          { "@name": "update", "@id": "https://api.nuget.org/v3-vulnerabilities/1/2/update.json" } ]
        """;

    private const string Base = """
        { "newtonsoft.json": [ { "url": "https://github.com/advisories/GHSA-aaaa", "severity": 2, "versions": "(, 13.0.1)" } ],
          "serilog": [ { "url": "https://github.com/advisories/GHSA-bbbb", "severity": 0, "versions": "[2.0.0, 2.1.0)" } ] }
        """;

    // The update replaces what the base said about a package, so serilog now has another advisory and newtonsoft.json keeps its own.
    private const string Update = """
        { "serilog": [ { "url": "https://github.com/advisories/GHSA-cccc", "severity": 3, "versions": "[2.0.0, 2.5.0]" } ] }
        """;

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static byte[] Zip(params (string Name, string Json)[] files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, json) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(json);
            }
        }

        return output.ToArray();
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Ok(string body) => Ok(Encoding.UTF8.GetBytes(body));

    private static AdvisoryFetcher Fetcher(Func<Uri, HttpResponseMessage> answer, out List<Uri> requested, long max = 1_000_000)
    {
        var seen = new List<Uri>();
        requested = seen;
        return new AdvisoryFetcher(
            new HttpClient(new FakeHandler(uri => { seen.Add(uri); return answer(uri); })),
            new VulnerabilitiesOptions { MaxDownloadBytes = max });
    }

    private static HttpResponseMessage FeedAnswer(Uri uri) => uri.AbsoluteUri switch
    {
        "https://api.nuget.org/v3/vulnerabilities/index.json" => Ok(Gzip(Index)),
        "https://api.nuget.org/v3-vulnerabilities/1/base.json" => Ok(Gzip(Base)),
        "https://api.nuget.org/v3-vulnerabilities/1/2/update.json" => Ok(Gzip(Update)),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    [Theory]
    [InlineData("(, 5.8.4)", "0", "5.8.4", null)]
    [InlineData("[2.1.0, 2.1.13)", "2.1.0", "2.1.13", null)]
    [InlineData("[2.2.0, 2.2.0]", "2.2.0", null, "2.2.0")]
    [InlineData("[1.0.0,]", "1.0.0", null, null)]
    public void TryParseRange_reads_the_interval_notation_of_nuget_as_an_osv_range(string notation, string introduced, string? fix, string? last)
    {
        // Act
        var ok = NuGetFeed.TryParseRange(notation, out var affected);

        // Assert
        ok.ShouldBeTrue();
        var events = affected.Ranges!.Single().Events!;
        events[0].Introduced.ShouldBe(introduced);
        events.Skip(1).Select(e => e.Fixed).SingleOrDefault().ShouldBe(fix);
        events.Skip(1).Select(e => e.LastAffected).SingleOrDefault().ShouldBe(last);
    }

    [Fact]
    public void TryParseRange_reads_a_single_version_and_refuses_what_is_no_notation()
    {
        // Act and Assert
        NuGetFeed.TryParseRange("[1.2.3]", out var one).ShouldBeTrue();
        one.Versions.ShouldBe(["1.2.3"]);
        NuGetFeed.TryParseRange("1.2.3", out _).ShouldBeFalse();
        NuGetFeed.TryParseRange("", out _).ShouldBeFalse();
        NuGetFeed.TryParseRange(null, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_nuget_feed_gives_advisories_that_match_like_any_other_and_the_update_replaces_the_base()
    {
        // Arrange
        var fetcher = Fetcher(FeedAnswer, out _);

        // Act
        var advisories = await fetcher.FetchAsync(new AdvisorySource("nuget", NuGetIndex), TestContext.Current.CancellationToken);

        // Assert
        advisories.Select(a => a.Id).Order().ShouldBe(["GHSA-aaaa", "GHSA-cccc"]);
        var newtonsoft = advisories.Single(a => a.Id == "GHSA-aaaa");
        newtonsoft.DatabaseSpecific!.Severity.ShouldBe("HIGH");
        AdvisoryMatcher.Match(newtonsoft, "Newtonsoft.Json", "12.0.3").ShouldBe((true, "13.0.1"));
        AdvisoryMatcher.Match(newtonsoft, "Newtonsoft.Json", "13.0.1").Affected.ShouldBeFalse();
        AdvisoryMatcher.Match(advisories.Single(a => a.Id == "GHSA-cccc"), "Serilog", "2.5.0").Affected.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_the_configured_address_and_the_pages_it_names_are_requested_and_nothing_about_the_packages_in_use_is_sent()
    {
        // Arrange
        var fetcher = Fetcher(FeedAnswer, out var requested);

        // Act
        await fetcher.FetchAsync(new AdvisorySource("nuget", NuGetIndex), TestContext.Current.CancellationToken);

        // Assert
        requested.Select(u => u.AbsoluteUri).ShouldBe(
        [
            "https://api.nuget.org/v3/vulnerabilities/index.json",
            "https://api.nuget.org/v3-vulnerabilities/1/base.json",
            "https://api.nuget.org/v3-vulnerabilities/1/2/update.json",
        ]);
        requested.ShouldAllBe(u => u.Query.Length == 0);
    }

    [Fact]
    public async Task A_page_on_another_host_than_the_source_is_not_followed()
    {
        // Arrange
        var fetcher = Fetcher(
            uri => uri == NuGetIndex ? Ok("""[ { "@name": "base", "@id": "https://evil.example.com/base.json" } ]""") : new HttpResponseMessage(HttpStatusCode.NotFound),
            out var requested);

        // Act
        var fetch = async () => await fetcher.FetchAsync(new AdvisorySource("nuget", NuGetIndex), TestContext.Current.CancellationToken);

        // Assert
        await fetch.ShouldThrowAsync<InvalidDataException>();
        requested.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_osv_zip_gives_only_the_advisories_about_nuget_packages()
    {
        // Arrange
        var zip = Zip(
            ("GHSA-1.json", """{ "id": "GHSA-1", "summary": "bad", "affected": [ { "package": { "ecosystem": "NuGet", "name": "Serilog" }, "versions": ["1.0.0"] } ] }"""),
            ("GHSA-2.json", """{ "id": "GHSA-2", "affected": [ { "package": { "ecosystem": "npm", "name": "left-pad" }, "versions": ["1.0.0"] } ] }"""),
            ("readme.txt", "not json"));
        var fetcher = Fetcher(_ => Ok(zip), out _);

        // Act
        var advisories = await fetcher.FetchAsync(new AdvisorySource("osv", OsvZip), TestContext.Current.CancellationToken);

        // Assert
        advisories.Select(a => a.Id).ShouldBe(["GHSA-1"]);
    }

    [Fact]
    public async Task A_source_that_sends_more_than_the_limit_is_refused_before_and_after_decompression()
    {
        // Arrange
        var big = new string('x', 5000);
        var plain = Fetcher(_ => Ok(big), out _, max: 1000);
        var compressed = Fetcher(_ => Ok(Gzip(big)), out _, max: 1000);

        // Act and Assert
        await Should.ThrowAsync<InvalidDataException>(async () => await plain.FetchAsync(new AdvisorySource("osv", OsvZip), TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidDataException>(async () => await compressed.FetchAsync(new AdvisorySource("osv", OsvZip), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_address_that_is_not_https_is_refused_without_a_request()
    {
        // Arrange
        var fetcher = Fetcher(_ => Ok("[]"), out var requested);

        // Act
        var fetch = async () => await fetcher.FetchAsync(new AdvisorySource("osv", new Uri("http://osv.example.org/all.json")), TestContext.Current.CancellationToken);

        // Assert
        await fetch.ShouldThrowAsync<InvalidDataException>();
        requested.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_source_that_fails_keeps_what_it_sent_before_and_does_not_stop_the_others()
    {
        // Arrange
        var failing = false;
        var options = new VulnerabilitiesOptions { Sources = [new AdvisorySource("osv", OsvZip), new AdvisorySource("nuget", NuGetIndex)] };
        var store = new AdvisoryStore(options, new FakeTimeProvider(), NullLogger<AdvisoryStore>.Instance);
        var osv = Zip(("a.json", """{ "id": "GHSA-1", "affected": [ { "package": { "ecosystem": "NuGet", "name": "Serilog" }, "versions": ["1.0.0"] } ] }"""));
        var fetcher = new AdvisoryFetcher(
            new HttpClient(new FakeHandler(uri => uri == OsvZip && failing ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : uri == OsvZip ? Ok(osv) : FeedAnswer(uri))),
            options);
        var service = new AdvisoryFetchService(fetcher, store, options, new FakeTimeProvider(), NullLogger<AdvisoryFetchService>.Instance);
        await service.FetchAllAsync(TestContext.Current.CancellationToken);
        store.Current.Advisories.Count.ShouldBe(3);

        // Act
        failing = true;
        await service.FetchAllAsync(TestContext.Current.CancellationToken);

        // Assert
        store.Current.Advisories.Select(a => a.Id).Order().ShouldBe(["GHSA-1", "GHSA-aaaa", "GHSA-cccc"]);
    }

    [Fact]
    public void An_advisory_that_a_file_and_a_download_both_have_is_kept_once_and_the_file_wins()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"adv-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{ "id": "GHSA-1", "summary": "from the file" }""");
        var store = new AdvisoryStore(new VulnerabilitiesOptions { Advisories = file }, new FakeTimeProvider(), NullLogger<AdvisoryStore>.Instance);
        store.Refresh();

        // Act
        store.SetFetched("osv", 0, [new OsvAdvisory("GHSA-1", "from osv"), new OsvAdvisory("GHSA-2", "only in osv")]);
        store.SetFetched("nuget", 1, [new OsvAdvisory("GHSA-2", "from nuget"), new OsvAdvisory("GHSA-3")]);

        // Assert
        var advisories = store.Current.Advisories.ToDictionary(a => a.Id);
        advisories.Keys.Order().ShouldBe(["GHSA-1", "GHSA-2", "GHSA-3"]);
        advisories["GHSA-1"].Summary.ShouldBe("from the file");
        advisories["GHSA-2"].Summary.ShouldBe("only in osv");
        File.Delete(file);
    }

    [Theory]
    [InlineData("ftp", "https://a.example.org/x.json")]
    [InlineData("osv", "http://a.example.org/x.json")]
    [InlineData("osv", "/relative/x.json")]
    public void AddVulnerabilities_refuses_a_source_of_an_unknown_kind_or_without_an_https_address(string kind, string url)
    {
        // Act
        var add = () => new ServiceCollection().AddRegistry().AddPackages().AddVulnerabilities(o => o.Sources = [new AdvisorySource(kind, new Uri(url, UriKind.RelativeOrAbsolute))]);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Nothing_is_fetched_unless_a_source_is_configured()
    {
        // Arrange
        using var none = new ServiceCollection().AddRegistry().AddPackages().AddVulnerabilities().BuildServiceProvider();
        using var some = new ServiceCollection().AddRegistry().AddPackages().AddVulnerabilities(o => o.Sources = [VulnerabilitiesOptions.NuGetFeed]).BuildServiceProvider();

        // Act and Assert
        none.GetService<AdvisoryFetchService>().ShouldBeNull();
        some.GetService<AdvisoryFetchService>().ShouldNotBeNull();
    }

    private sealed class FakeHandler(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = answer(request.RequestUri!);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
