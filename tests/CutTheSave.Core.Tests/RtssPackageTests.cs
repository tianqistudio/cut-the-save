using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutTheSave.Core;

internal static class RtssPackageTests
{
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("Synthetic fixture only, never executable");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static RtssPackageManifest Manifest(byte[]? bytes = null) => new(1, "7.3.5.28314", "RTSSSetup-test.exe",
        (bytes ?? Payload).Length, Hash(bytes ?? Payload), "https://www.guru3d.com/test-package", "RTSSSetup-test.exe", (bytes ?? Payload).Length, Hash(bytes ?? Payload));

    internal static void Run()
    {
        var root = Directory.CreateTempSubdirectory("CutTheSave-rtss-download-tests-");
        try { RunAsync(root.FullName).GetAwaiter().GetResult(); }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
    }

    private static async Task RunAsync(string root)
    {
        var metadata = Manifest();
        var json = JsonSerializer.SerializeToUtf8Bytes(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Check(RtssPackageManifest.Parse(json) == metadata, "manifest round-trip");
        foreach (var bad in new[] { "null", "{}", "{\"schemaVersion\":1,\"schemaVersion\":2}", "{\"size\":1e999}", "{\"unknown\":true}" })
            Throws<FormatException>(() => RtssPackageManifest.Parse(Encoding.UTF8.GetBytes(bad)));
        Throws<FormatException>(() => RtssPackageManifest.Parse(new byte[65537]));
        foreach (var name in new[] { "../test.exe", "a/b.exe", @"a\b.exe", "C:evil.exe", "NUL.exe", "COM1.exe", "test.exe.", "test.exe ", "test.exe:stream", "test\0.exe" })
            Throws<FormatException>(() => (metadata with { FileName = name, InstallerFileName = name }).Validate());
        foreach (var uri in new[] { "http://www.guru3d.com/x", "https://guru3d.com.evil.test/x", "https://evilguru3d.com/x",
            "https://www.guru3d.com:444/x", "https://user:secret@guru3d.com/x", "https://guru3d.com/x#fragment", "file:///C:/x", "https://127.0.0.1/x" })
            Throws<FormatException>(() => (metadata with { OfficialUrl = uri }).Validate());
        foreach (var bad in new[] { metadata with { SchemaVersion = 2 }, metadata with { Version = "7.3.5-beta" },
            metadata with { Size = 0 }, metadata with { Size = long.MaxValue }, metadata with { InstallerSize = -1 },
            metadata with { InstallerSha256 = new string('f', 64) }, metadata with { Sha256 = new string('z', 64) } })
            Throws<FormatException>(bad.Validate);

        using (var handler = new Transport((request, n) => n == 0 ? Redirect("/relative") : Ok(Payload)))
        using (var client = new RtssPackageDownloader(handler))
        {
            var package = await client.DownloadAsync(metadata, Path.Combine(root, "success"));
            Check(handler.Requests.Count == 2 && handler.Requests[1].AbsolutePath == "/relative", "manual relative redirect");
            using (var lease = package.OpenVerifiedInstaller())
            {
                Check(lease.Length == Payload.Length, "verified installer length");
                if (OperatingSystem.IsWindows()) Throws<IOException>(() => File.WriteAllBytes(package.InstallerPath, Payload));
            }
            if (OperatingSystem.IsWindows()) Check(File.ReadAllText(package.InstallerPath + ":Zone.Identifier").Contains("ZoneId=3"), "Windows origin warning preserved");
            File.WriteAllBytes(package.InstallerPath, new byte[Payload.Length]);
            Throws<IOException>(() => package.OpenVerifiedInstaller());
        }

        var cases = new (string Name, Func<HttpRequestMessage, int, HttpResponseMessage> Send, int Count)[]
        {
            ("external redirect", (_, _) => Redirect("https://evil.test/payload"), 1),
            ("downgrade redirect", (_, _) => Redirect("http://guru3d.com/payload"), 1),
            ("credential redirect", (_, _) => Redirect("https://user@guru3d.com/payload"), 1),
            ("loop", (_, _) => Redirect(metadata.OfficialUrl), 1),
            ("too many redirects", (_, n) => Redirect("/hop" + n), 6),
            ("forbidden", (_, _) => new(HttpStatusCode.Forbidden), 1),
            ("partial response", (_, _) => new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload) }, 1),
            ("missing redirect location", (_, _) => new(HttpStatusCode.Found), 1),
            ("wrong hash", (_, _) => Ok(new byte[Payload.Length]), 1),
            ("truncated response", (_, _) => Ok(Payload[..^1]), 1),
            ("hidden oversize", (_, _) => { var response = Ok(new byte[Payload.Length + 1]); response.Content.Headers.ContentLength = Payload.Length; return response; }, 1),
            ("no length oversize", (_, _) => new(HttpStatusCode.OK) { Content = new StreamContent(new NoLengthStream(new byte[Payload.Length + 1])) }, 1),
            ("encoded response", (_, _) => { var response = Ok(Payload); response.Content.Headers.ContentEncoding.Add("gzip"); return response; }, 1),
            ("auto redirect transport", (_, _) => { var response = Ok(Payload); response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://guru3d.com/jumped"); return response; }, 1),
            ("stream failure", (_, _) => new(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) }, 1)
        };
        foreach (var item in cases)
        {
            var folder = Path.Combine(root, item.Name);
            using var handler = new Transport(item.Send);
            using var client = new RtssPackageDownloader(handler);
            await Fails(() => client.DownloadAsync(metadata, folder));
            Check(handler.Requests.Count == item.Count, "request boundary: " + item.Name);
            Check(!Directory.EnumerateFileSystemEntries(folder).Any(), "failed bytes removed: " + item.Name);
        }

        using (var canceled = new CancellationTokenSource())
        using (var handler = new Transport((_, _) => new(HttpStatusCode.OK) { Content = new StreamContent(new CancelingStream(canceled)) }))
        using (var client = new RtssPackageDownloader(handler))
        {
            await Fails(() => client.DownloadAsync(metadata, Path.Combine(root, "canceled"), cancellationToken: canceled.Token));
            Check(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "canceled")).Any(), "cancellation removes partial bytes");
            var untouched = Path.Combine(root, "pre-canceled");
            await Fails(() => client.DownloadAsync(metadata, untouched, cancellationToken: canceled.Token));
            Check(!Directory.Exists(untouched) && handler.Requests.Count == 1, "pre-canceled download does not touch disk or network");
        }

        foreach (var kind in new[] { "valid", "duplicate", "traversal", "inner-hash", "inner-size", "symlink", "excess-entries" })
        {
            var bytes = Zip(kind, metadata.InstallerFileName);
            var zipped = metadata with { FileName = "[Guru3D]-RTSS-test.zip", Size = bytes.Length, Sha256 = Hash(bytes) };
            var folder = Path.Combine(root, "zip-" + kind);
            using var client = new RtssPackageDownloader(new Transport((_, _) => Ok(bytes)));
            if (kind == "valid")
            {
                var package = await client.DownloadAsync(zipped, folder);
                using (package.OpenVerifiedInstaller()) { }
                Check(Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length == 1, "only reviewed installer extracted, no unrelated archive entries");
            }
            else
            {
                await Fails(() => client.DownloadAsync(zipped, folder));
                Check(!Directory.EnumerateFileSystemEntries(folder).Any(), "bad ZIP leaves no installer: " + kind);
            }
        }
        using (var client = new RtssPackageDownloader(new Transport((_, _) => new(HttpStatusCode.OK)
        { Content = new StreamContent(new StallingStream()) }), TimeSpan.FromMilliseconds(100)))
        {
            await Fails(() => client.DownloadAsync(metadata, Path.Combine(root, "body-timeout")));
            Check(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "body-timeout")).Any(), "body timeout removes partial bytes");
        }
        Console.WriteLine("PASS RTSS strict metadata, 15 transport failures, cancellation, ZIP verification, launch-byte lease and origin marker");
    }

    private static byte[] Zip(string kind, string name)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(kind == "traversal" ? "../" + name : name);
            if (kind == "symlink") entry.ExternalAttributes = 0xA000 << 16;
            using (var stream = entry.Open()) stream.Write(kind == "inner-hash" ? new byte[Payload.Length] : kind == "inner-size" ? new byte[Payload.Length + 1] : Payload);
            if (kind == "duplicate") { using var stream = zip.CreateEntry(name.ToUpperInvariant()).Open(); stream.Write(Payload); }
            for (var index = 0; index < (kind == "excess-entries" ? 129 : 1); index++)
            { using var stream = zip.CreateEntry("unrelated-" + index + ".exe").Open(); stream.WriteByte(1); }
        }
        return output.ToArray();
    }
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string uri)
    { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(uri, UriKind.RelativeOrAbsolute); return response; }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Fails(Func<Task<VerifiedRtssPackage>> action)
    { try { await action(); } catch (Exception exception) when (exception is IOException or HttpRequestException or OperationCanceledException) { return; } throw new Exception("Unsafe package accepted"); }

    private sealed class Transport(Func<HttpRequestMessage, int, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var result = send(request, Requests.Count - 1);
            result.RequestMessage ??= request;
            return Task.FromResult(result);
        }
    }
    private sealed class NoLengthStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base(Payload) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("Synthetic broken connection");
    }
    private sealed class CancelingStream(CancellationTokenSource cancellation) : MemoryStream(Payload)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellation.Cancel(); return ValueTask.FromCanceled<int>(cancellationToken); }
    }
    private sealed class StallingStream : MemoryStream
    {
        public StallingStream() : base(Payload) { }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
}
