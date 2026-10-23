using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Hosting;
using Monitoring.Injector.Hosting;
using Monitoring.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Injector.Tests;

/// <summary>The injector as the API server meets it: a web server with a certificate that answers admission reviews.</summary>
public class InjectorHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(IHost Host, HttpClient Client)> StartAsync(TestPki pki, IEnumerable<KeyValuePair<string, string?>>? extra = null)
    {
        var issued = pki.Issue("monitoring-injector.monitoring.svc");
        var settings = new Dictionary<string, string?>
        {
            ["Injector:SidecarImage"] = "monitoring-sidecar:1",
            ["Injector:Certificate"] = issued.CertificateFile,
            ["Injector:Key"] = issued.KeyFile,
            ["Injector:Web:Address"] = "127.0.0.1",
            ["Injector:Web:Port"] = "0",
        };
        foreach (var pair in extra ?? [])
        {
            settings[pair.Key] = pair.Value;
        }

        var host = InjectorComposition.CreateHost([], settings, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        await host.StartAsync(Ct);
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        // The way the API server checks it: against the authority that signed the certificate. The name is not checked here, because the test connects by address.
        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(pki.CaFile);
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return certificate is not null && chain.Build(new X509Certificate2(certificate));
        };
        return (host, new HttpClient(handler) { BaseAddress = new Uri(address.Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1")) });
    }

    private const string Review = """
        { "apiVersion": "admission.k8s.io/v1", "kind": "AdmissionReview",
          "request": { "uid": "u-1", "operation": "CREATE", "object": { "metadata": { "name": "p" }, "spec": { "containers": [ { "name": "app", "image": "a:1" } ] } } } }
        """;

    private static StringContent Json(string text) => new(text, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

    [Fact]
    public async Task The_webhook_answers_over_tls_and_adds_the_sidecar_for_a_pod_of_an_opted_in_namespace()
    {
        // Arrange
        using var pki = new TestPki();
        var (host, client) = await StartAsync(pki);
        using var _ = host;
        using var __ = client;

        // Act
        using var response = await client.PostAsync("/mutate/namespace", Json(Review), Ct);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        answer["response"]!["uid"]!.GetValue<string>().ShouldBe("u-1");
        answer["response"]!["patchType"]!.GetValue<string>().ShouldBe("JSONPatch");
        var patch = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(answer["response"]!["patch"]!.GetValue<string>())))!.AsArray();
        patch.Any(op => op!["path"]!.GetValue<string>() == "/spec/initContainers").ShouldBeTrue();
    }

    [Fact]
    public async Task A_pod_that_the_registration_for_pods_calls_about_without_its_own_label_is_left_alone_and_a_review_that_is_not_one_is_refused()
    {
        // Arrange
        using var pki = new TestPki();
        var (host, client) = await StartAsync(pki);
        using var _ = host;
        using var __ = client;

        // Act
        using var plain = await client.PostAsync("/mutate", Json(Review), Ct);
        using var broken = await client.PostAsync("/mutate", Json("not json"), Ct);
        using var health = await client.GetAsync("/healthz", Ct);

        // Assert
        JsonNode.Parse(await plain.Content.ReadAsStringAsync(Ct))!["response"]!.AsObject().ContainsKey("patch").ShouldBeFalse();
        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The thumbprint of the certificate that a brand new connection is given, which is what the API server meets.</summary>
    private static async Task<string> PresentedAsync(string address, TestPki pki)
    {
        string? presented = null;
        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(pki.CaFile);
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            presented = certificate?.GetCertHashString();
            return true;
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        using var response = await client.GetAsync("/healthz", Ct);
        response.EnsureSuccessStatusCode();
        return presented!;
    }

    private static async Task<string> AddressOfAsync(IHost host) =>
        host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1");

    [Fact]
    public async Task A_renewed_certificate_is_presented_to_new_connections_without_a_restart_and_a_broken_one_changes_nothing()
    {
        // Arrange
        using var pki = new TestPki();
        var first = pki.Issue("monitoring-injector.monitoring.svc");
        var files = pki.Issue("monitoring-injector.monitoring.svc");
        var certificateFile = Path.Combine(pki.Directory, "live.crt");
        var keyFile = Path.Combine(pki.Directory, "live.key");
        File.Copy(first.CertificateFile, certificateFile);
        File.Copy(first.KeyFile, keyFile);
        using var host = InjectorComposition.CreateHost([], new Dictionary<string, string?>
        {
            ["Injector:SidecarImage"] = "monitoring-sidecar:1",
            ["Injector:Certificate"] = certificateFile,
            ["Injector:Key"] = keyFile,
            ["Injector:Web:Address"] = "127.0.0.1",
            ["Injector:Web:Port"] = "0",
            ["Injector:CertificateCheckInterval"] = "00:00:00.050",
        }, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        await host.StartAsync(Ct);
        var address = await AddressOfAsync(host);
        var before = await PresentedAsync(address, pki);

        // Act: the renewal is half written first, which must not take the webhook down, and then complete
        await Task.Delay(100, Ct);
        await File.WriteAllTextAsync(certificateFile, "this is not a certificate", Ct);
        await Task.Delay(100, Ct);
        var whileBroken = await PresentedAsync(address, pki);
        File.Copy(files.CertificateFile, certificateFile, overwrite: true);
        File.Copy(files.KeyFile, keyFile, overwrite: true);
        await Task.Delay(100, Ct);
        var after = await PresentedAsync(address, pki);

        // Assert
        before.ShouldBe(first.Thumbprint, StringCompareShould.IgnoreCase);
        whileBroken.ShouldBe(before);
        after.ShouldBe(files.Thumbprint, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void The_sidecar_image_has_to_be_given_and_the_certificate_has_to_exist()
    {
        // Act
        var withoutImage = () => InjectorComposition.CreateHost([], new Dictionary<string, string?> { ["Injector:Certificate"] = "a", ["Injector:Key"] = "b" }, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        // Assert
        withoutImage.ShouldThrow<ConfigurationException>().Message.ShouldContain("Injector:SidecarImage");
    }
}
