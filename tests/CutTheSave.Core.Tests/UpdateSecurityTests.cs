using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CutTheSave.Core;
using NSec.Cryptography;

internal static class UpdateSecurityTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("synthetic update, not executable");
    internal static UpdateManifest Manifest(string version = "2.0.0") => new(1, "test-key", "stable", version,
        new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), version, [],
        [new("win-x64", "CutTheSave.zip", Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)),
            [new("a", "https://a.example/asset.zip"), new("b", "https://b.example/asset.zip")])]);

    public static void Run()
    {
        MetadataAndDecisions();
        RunAsync().GetAwaiter().GetResult();
    }

    private static void MetadataAndDecisions()
    {
        var original = UpdateManifestCodec.Serialize(Manifest());
        var mutations = new Action<JsonNode>[]
        {
            n => n["schemaVersion"] = 2, n => n["version"] = "2.0", n => n["recommendedVersion"] = null,
            n => n["publishedAt"] = "0001-01-01T00:00:00Z", n => n["unknown"] = true,
            n => n["assets"] = new JsonArray(), n => n["blockedVersions"] = new JsonArray("oops"),
            n => n["assets"]![0]!["size"] = 0, n => n["assets"]![0]!["size"] = UpdateManifestCodec.MaximumAssetSize + 1,
            n => n["assets"]![0]!["fileName"] = "../evil.zip", n => n["assets"]![0]!["fileName"] = "NUL.zip",
            n => n["assets"]![0]!["fileName"] = "x:evil.zip", n => n["assets"]![0]!["sha256"] = null,
            n => n["assets"]![0]!["mirrors"]![0]!["url"] = "http://a.example/x",
            n => n["assets"]![0]!["mirrors"]![0]!["url"] = "https://user@a.example/x",
            n => n["assets"]![0]!["mirrors"]![0]!["url"] = "https://a.example:444/x",
            n => n["assets"]![0]!["mirrors"]![0]!["url"] = "https://a.example/x#fragment",
            n => ((JsonArray)n["assets"]!).Add(n["assets"]![0]!.DeepClone()),
            n => ((JsonArray)n["assets"]![0]!["mirrors"]!).Add(n["assets"]![0]!["mirrors"]![0]!.DeepClone())
        };
        foreach (var change in mutations)
        {
            var node = JsonNode.Parse(original)!;
            change(node);
            Reject(() => UpdateManifestCodec.Parse(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        var text = Encoding.UTF8.GetString(original);
        Reject(() => UpdateManifestCodec.Parse(Encoding.UTF8.GetBytes(text.Replace("\"version\":", "\"Version\":\"3.0.0\",\"version\":"))));
        Reject(() => UpdateManifestCodec.Parse(Encoding.UTF8.GetBytes(text.Replace("\"sha256\":", "\"SHA256\":\"bad\",\"sha256\":"))));
        Reject(() => UpdateManifestCodec.Parse(new byte[65537]));
        Reject(() => UpdateManifestCodec.Parse("{"u8.ToArray()));
        Reject(() => UpdateManifestCodec.Parse("null"u8.ToArray()));

        VerifiedManifestSource Source(string id, UpdateManifest m) => new(id, m, UpdateManifestCodec.Serialize(m));
        var first = Manifest();
        var asset = first.Assets[0];
        foreach (var conflict in new[] { asset with { Sha256 = new string('a', 64) }, asset with { Size = asset.Size + 1 }, asset with { FileName = "other.zip" } })
        {
            var other = first with { Version = "2.0.0.0", Assets = [conflict], BlockedVersions = ["2.0.0"] };
            Check(new UpdateDecisionService().Choose("1.0.0", [Source("a", first), Source("b", other)]).Kind == UpdateDecisionKind.SecurityConflict,
                "normalized same-version identity conflict cannot be hidden by blocklist");
        }
        var withdrawal = Manifest("3.0.0") with { BlockedVersions = ["2.0.0.0", "3.0.0"] };
        Check(new UpdateDecisionService().Choose("1.0.0", [Source("a", first), Source("b", withdrawal)]).Kind == UpdateDecisionKind.NoValidSource,
            "new signed withdrawal blocks stale source");
        Check(new UpdateDecisionService().Choose("2.0.0.0", [Source("a", first)]).Kind == UpdateDecisionKind.AlreadyCurrent, "no equivalent-version reinstall");
        Check(new UpdateDecisionService().Choose("3.0.0", [Source("a", first)]).Kind == UpdateDecisionKind.AlreadyCurrent, "no downgrade");
        Console.WriteLine("PASS update metadata bounds, duplicate fields, normalized conflicts and cross-source withdrawal");
    }

    private static async Task RunAsync()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var verifier = new Ed25519ManifestVerifier(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var good = UpdateManifestCodec.Serialize(Manifest());
        var endpoints = new[] { ("a", new Uri("https://a.example/manifest"), new Uri("https://a.example/signature")),
            ("b", new Uri("https://b.example/manifest"), new Uri("https://b.example/signature")) };
        foreach (var fault in new[] { "http", "network", "headers-timeout", "body-timeout", "bad-signature", "malformed", "key-id", "channel", "oversized", "encoding", "redirect" })
        foreach (var badHost in new[] { "a.example", "b.example" })
        {
            var bytes = fault switch
            {
                "malformed" => "{"u8.ToArray(),
                "key-id" => UpdateManifestCodec.Serialize(Manifest() with { KeyId = "other-key" }),
                "channel" => UpdateManifestCodec.Serialize(Manifest() with { Channel = "preview" }),
                _ => good
            };
            var requests = new ConcurrentBag<Uri>();
            using var handler = new Handler(async (request, token) =>
            {
                var uri = request.RequestUri!;
                requests.Add(uri);
                var bad = uri.Host == badHost;
                if (bad && fault == "network") throw new HttpRequestException("synthetic disconnect");
                if (bad && fault == "headers-timeout") await Task.Delay(Timeout.Infinite, token);
                var data = bad ? bytes : good;
                if (uri.AbsolutePath == "/signature")
                    data = bad && fault == "bad-signature" ? new byte[64] : SignatureAlgorithm.Ed25519.Sign(key, data);
                var response = Response(request, data);
                if (bad && fault == "http") response.StatusCode = HttpStatusCode.ServiceUnavailable;
                if (bad && fault == "body-timeout") response.Content = new StreamContent(new StalledStream());
                if (bad && fault == "oversized") response.Content.Headers.ContentLength = 65537;
                if (bad && fault == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
                if (bad && fault == "redirect") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("https://evil.example/manifest"); }
                return response;
            });
            using var client = new UpdateSourceClient(handler, verifier, ["a.example", "b.example"], "test-key", "stable", TimeSpan.FromMilliseconds(200));
            var sources = await client.FetchAllAsync(endpoints);
            Check(sources.Count == 1 && sources[0].SourceId == (badHost == "a.example" ? "b" : "a"), $"healthy source survives {fault} in either position");
            Check(requests.All(uri => uri.Host is "a.example" or "b.example"), "unapproved redirect never requested");
        }
        using (var client = new UpdateSourceClient(new Handler((request, _) => Task.FromResult(Response(request, request.RequestUri!.AbsolutePath == "/signature"
            ? Encoding.UTF8.GetBytes(Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, good))) : good))), verifier, ["a.example", "b.example"], "test-key", "stable"))
            Check((await client.FetchAllAsync(endpoints)).Count == 2, "both sources and Base64 signatures accepted");
        using (var client = new UpdateSourceClient(new Handler((_, _) => throw new HttpRequestException("offline")), verifier, ["a.example", "b.example"], "test-key", "stable"))
            Check((await client.FetchAllAsync(endpoints)).Count == 0, "both offline sources return no update");
        var conflicting = UpdateManifestCodec.Serialize(Manifest() with { Assets = [Manifest().Assets[0] with { Sha256 = new string('b', 64) }] });
        using (var client = new UpdateSourceClient(new Handler((request, _) =>
        {
            var data = request.RequestUri!.Host == "a.example" ? good : conflicting;
            return Task.FromResult(Response(request, request.RequestUri.AbsolutePath == "/signature" ? SignatureAlgorithm.Ed25519.Sign(key, data) : data));
        }), verifier, ["a.example", "b.example"], "test-key", "stable"))
            Check(new UpdateDecisionService().Choose("1.0.0", await client.FetchAllAsync(endpoints)).Kind == UpdateDecisionKind.SecurityConflict,
                "two actual verified signatures feed the conflict decision");
        foreach (var signatureData in new[] { new byte[1025], Encoding.UTF8.GetBytes("not Base64") })
        using (var client = new UpdateSourceClient(new Handler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsolutePath == "/signature" ? signatureData : good))), verifier, ["a.example", "b.example"], "test-key", "stable"))
            Check((await client.FetchAllAsync(endpoints)).Count == 0, "signature size/encoding rejected");
        var oversizedRequests = 0;
        using (var client = new UpdateSourceClient(new Handler((request, _) =>
        {
            oversizedRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(new NoLengthStream(new byte[65537])) });
        }), verifier, ["a.example", "b.example"], "test-key", "stable"))
            Check((await client.FetchAllAsync(endpoints)).Count == 0 && oversizedRequests == 2, "unbounded-length manifest stops before fetching signature");
        using (var cancel = new CancellationTokenSource())
        using (var client = new UpdateSourceClient(new Handler((_, _) => throw new InvalidOperationException("precancel contacted transport")), verifier, ["a.example", "b.example"], "test-key", "stable"))
        {
            cancel.Cancel();
            try { await client.FetchAllAsync(endpoints, cancel.Token); throw new InvalidOperationException("precancel ignored"); }
            catch (OperationCanceledException) { }
            try { await client.DownloadAssetAsync(Manifest().Assets[0], cancel.Token); throw new InvalidOperationException("asset precancel ignored"); }
            catch (OperationCanceledException) { }
        }
        await RedirectAndAssetTests(verifier);
        using (var cancel = new CancellationTokenSource())
        using (var client = new UpdateSourceClient(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { RequestMessage = request, Content = new StreamContent(new StalledStream()) })), verifier, ["a.example", "b.example"], "test-key", "stable"))
        {
            cancel.CancelAfter(30);
            try { await client.FetchAllAsync(endpoints, cancel.Token); throw new InvalidOperationException("Caller cancellation swallowed"); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
        Console.WriteLine("PASS 22 dual-source failure combinations, offline/both-good sources, body deadlines and caller cancellation");
    }

    private static async Task RedirectAndAssetTests(Ed25519ManifestVerifier verifier)
    {
        var asset = Manifest().Assets[0] with { Mirrors = [new("a", "https://a.example/asset.zip")] };
        foreach (var location in new[] { "http://a.example/x", "https://evil.example/x", "https://user@a.example/x", "https://a.example:444/x", "https://a.example/x#f", "https://a.example/asset.zip" })
        {
            var count = 0;
            using var client = new UpdateSourceClient(new Handler((request, _) =>
            {
                count++;
                var response = Response(request, []);
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri(location);
                return Task.FromResult(response);
            }), verifier, ["a.example"], "test-key", "stable");
            Check(await client.DownloadAssetAsync(asset) is null && count == 1, "unsafe/loop redirect rejected before next request");
        }
        foreach (var mode in new[] { "ok", "wrong-hash", "truncated", "overlong", "partial", "silent-redirect", "encoding", "stall", "io-error" })
        {
            using var client = new UpdateSourceClient(new Handler((request, _) =>
            {
                if (mode == "io-error") throw new IOException("synthetic failed stream");
                var payload = mode == "wrong-hash" ? new byte[Payload.Length] : mode == "truncated" ? Payload[..^1] : mode == "overlong" ? [.. Payload, (byte)0] : Payload;
                var response = Response(request, payload);
                if (mode is "truncated" or "overlong") response.Content = new StreamContent(new NoLengthStream(payload));
                if (mode == "partial") response.StatusCode = HttpStatusCode.PartialContent;
                if (mode == "silent-redirect") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://a.example/silent");
                if (mode == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
                if (mode == "stall") response.Content = new StreamContent(new StalledStream());
                return Task.FromResult(response);
            }), verifier, ["a.example"], "test-key", "stable", TimeSpan.FromMilliseconds(200));
            var result = await client.DownloadAssetAsync(asset);
            Check(mode == "ok" ? result is not null && result.SequenceEqual(Payload) : result is null, $"bounded asset {mode}");
        }
        var hops = 0;
        using (var client = new UpdateSourceClient(new Handler((request, _) =>
        {
            var response = Response(request, []);
            response.StatusCode = HttpStatusCode.TemporaryRedirect;
            response.Headers.Location = new Uri("/hop-" + ++hops, UriKind.Relative);
            return Task.FromResult(response);
        }), verifier, ["a.example"], "test-key", "stable"))
            Check(await client.DownloadAssetAsync(asset) is null && hops == 6, "sixth redirect not followed");
        var requests = new List<Uri>();
        using (var client = new UpdateSourceClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            var response = Response(request, Payload);
            if (request.RequestUri!.Host == "a.example") response.Content = new StreamContent(new StalledStream());
            else if (request.RequestUri.AbsolutePath == "/asset.zip") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("/final.zip", UriKind.Relative); }
            return Task.FromResult(response);
        }), verifier, ["a.example", "b.example"], "test-key", "stable", TimeSpan.FromMilliseconds(200)))
            Check((await client.DownloadAssetAsync(Manifest().Assets[0]))!.SequenceEqual(Payload) && requests.Count == 3, "timed-out mirror falls back with approved relative redirect");
        Console.WriteLine("PASS per-hop asset URL checks, exact size/hash, partial/encoded/oversized responses and mirror timeout fallback");
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] data) => new(HttpStatusCode.OK)
        { RequestMessage = request, Content = new ByteArrayContent(data) };
    private static void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    private static void Reject(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new InvalidOperationException("Malformed update metadata accepted");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class NoLengthStream(byte[] data) : MemoryStream(data) { public override bool CanSeek => false; }
    private sealed class StalledStream : MemoryStream
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
}
