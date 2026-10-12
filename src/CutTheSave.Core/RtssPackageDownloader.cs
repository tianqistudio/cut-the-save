using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace CutTheSave.Core;

public sealed class RtssPackageCleanupException(IReadOnlyList<string> paths, Exception original, IReadOnlyList<Exception> cleanup)
    : IOException("An unverified RTSS download could not be removed. Do not run it.", new AggregateException(new[] { original }.Concat(cleanup)))
{
    public IReadOnlyList<string> RetainedPaths { get; } = paths.ToArray();
}

public sealed class VerifiedRtssPackage
{
    public string InstallerPath { get; }
    public RtssPackageManifest Manifest { get; }
    internal VerifiedRtssPackage(string path, RtssPackageManifest manifest) { InstallerPath = path; Manifest = manifest; }

    // Keep this lease open through the explicit installer launch. Recheck the
    // exact bytes, not a filename or a prior successful download message.
    public FileStream OpenVerifiedInstaller()
    {
        Manifest.Validate();
        RtssPackageDownloader.EnsureNoLinks(InstallerPath);
        var stream = new FileStream(InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != Manifest.InstallerSize ||
                !Convert.ToHexString(SHA256.HashData(stream)).Equals(Manifest.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The downloaded RTSS installer changed; download it again.");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}

public sealed class RtssPackageDownloader : IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    public RtssPackageDownloader() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None
    }) { }

    // Tests inject a transport, not alternate hosts or a validation bypass.
    internal RtssPackageDownloader(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(3);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(3)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CutTheSave/1.0");
    }

    public async Task<VerifiedRtssPackage> DownloadAsync(RtssPackageManifest manifest, string cacheRoot,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        // ResponseHeadersRead does not bound body reads with HttpClient.Timeout.
        // One deadline covers redirects, transfer, extraction and file writes.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        cancellationToken = deadline.Token;
        var root = Path.GetFullPath(cacheRoot);
        EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        EnsureNoLinks(root);
        var folder = Path.Combine(root, "rtss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var packagePart = Path.Combine(folder, "package.part");
        var installerPart = Path.Combine(folder, "installer.part");
        var final = Path.Combine(folder, manifest.InstallerFileName);
        try
        {
            using (var response = await FetchAsync(new Uri(manifest.OfficialUrl), cancellationToken).ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentEncoding.Count > 0 ||
                    response.Content.Headers.ContentLength is long length && length != manifest.Size)
                    throw new IOException("RTSS response size or content encoding disagrees with the reviewed manifest.");
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await WriteVerifiedAsync(source, packagePart, manifest.Size, manifest.Sha256, progress, cancellationToken).ConfigureAwait(false);
            }
            if (manifest.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var archive = ZipFile.OpenRead(packagePart))
                {
                    if (archive.Entries.Count > 128) throw new IOException("RTSS archive contains too many entries.");
                    var matches = archive.Entries.Where(entry => entry.FullName.Equals(manifest.InstallerFileName, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1 || matches[0].FullName != manifest.InstallerFileName ||
                        matches[0].Length != manifest.InstallerSize || ((matches[0].ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("RTSS archive does not contain exactly the reviewed installer.");
                    // Never ExtractToDirectory: only the one reviewed root entry is read.
                    using var source = matches[0].Open();
                    await WriteVerifiedAsync(source, installerPart, manifest.InstallerSize, manifest.InstallerSha256, null, cancellationToken).ConfigureAwait(false);
                }
                File.Delete(packagePart);
            }
            else File.Move(packagePart, installerPart);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(folder);
            // Preserve Windows origin warnings; do not unblock downloaded code.
            if (OperatingSystem.IsWindows())
                await File.WriteAllTextAsync(installerPart + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", cancellationToken).ConfigureAwait(false);
            File.Move(installerPart, final, overwrite: false);
            var result = new VerifiedRtssPackage(final, manifest);
            using (result.OpenVerifiedInstaller()) { }
            progress?.Report(1);
            return result;
        }
        catch (Exception original)
        {
            var retained = new List<string>();
            var errors = new List<Exception>();
            foreach (var path in new[] { packagePart, installerPart, final })
            {
                try { EnsureNoLinks(path); if (File.Exists(path)) File.Delete(path); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                { retained.Add(path); errors.Add(cleanup); }
            }
            try { EnsureNoLinks(folder); if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { retained.Add(folder); errors.Add(cleanup); }
            if (retained.Count > 0) throw new RtssPackageCleanupException(retained, original, errors);
            throw;
        }
    }

    private async Task<HttpResponseMessage> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var hop = 0; hop <= 5; hop++)
        {
            if (!RtssPackageManifest.IsAllowedSource(uri) || !seen.Add(uri.AbsoluteUri))
                throw new IOException("RTSS redirect is unsafe or loops.");
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            try
            {
                if (response.RequestMessage?.RequestUri != uri)
                    throw new IOException("RTSS transport unexpectedly followed a redirect.");
                if (response.StatusCode == HttpStatusCode.OK) return response;
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) ||
                    response.Headers.Location is not Uri location || hop == 5)
                    throw new HttpRequestException("The official RTSS source did not return a downloadable file.", null, response.StatusCode);
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            }
            catch { response.Dispose(); throw; }
            response.Dispose();
        }
        throw new IOException("Too many RTSS redirects.");
    }

    private static async Task WriteVerifiedAsync(Stream source, string target, long size, string expectedHash,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        var buffer = new byte[65536];
        long count = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
            if (count > size) throw new IOException("RTSS download exceeds the reviewed size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report((double)count / size);
        }
        if (count != size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("RTSS download size or SHA-256 check failed.");
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    internal static void EnsureNoLinks(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("RTSS cache must use an absolute path.");
        for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("RTSS cache cannot cross a link or reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public void Dispose() => _client.Dispose();
}
