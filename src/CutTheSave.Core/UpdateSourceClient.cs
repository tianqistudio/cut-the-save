using System.Net;
using System.Security.Cryptography;

namespace CutTheSave.Core;

public sealed class UpdateSourceClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly Ed25519ManifestVerifier _verifier;
    private readonly string[] _allowedHosts;
    private readonly string _keyId;
    private readonly string _channel;
    private readonly TimeSpan _sourceTimeout;
    private readonly TimeSpan _mirrorTimeout;

    public UpdateSourceClient(Ed25519ManifestVerifier verifier, IEnumerable<string> allowedHosts, string keyId, string channel)
        : this(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32
        }, verifier, allowedHosts, keyId, channel) { }

    internal UpdateSourceClient(HttpMessageHandler handler, Ed25519ManifestVerifier verifier,
        IEnumerable<string> allowedHosts, string keyId, string channel, TimeSpan? testTimeout = null)
    {
        _allowedHosts = allowedHosts.ToArray();
        if (_allowedHosts.Length == 0 || _allowedHosts.Any(host => !IsValidHost(host)))
            throw new ArgumentException("An exact HTTPS host allowlist is required.", nameof(allowedHosts));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        if (testTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(20)))
            throw new ArgumentOutOfRangeException(nameof(testTimeout));
        _verifier = verifier;
        _keyId = keyId;
        _channel = channel;
        _sourceTimeout = testTimeout ?? TimeSpan.FromSeconds(20);
        _mirrorTimeout = testTimeout ?? TimeSpan.FromSeconds(60);
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CutTheSave/1.0");
    }

    public static bool IsValidHost(string host) => !string.IsNullOrWhiteSpace(host) &&
        Uri.CheckHostName(host) == UriHostNameType.Dns && !host.EndsWith('.') &&
        host.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');

    public static bool IsAllowedUri(Uri uri, IEnumerable<string> allowedHosts) => uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        allowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<VerifiedManifestSource>> FetchAllAsync(
        IEnumerable<(string Id, Uri Manifest, Uri Signature)> sources, CancellationToken cancellationToken = default)
    {
        var endpoints = sources.ToArray();
        if (endpoints.Length != 2 || endpoints.Select(source => source.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new ArgumentException("Exactly two distinct signed sources are required.", nameof(sources));
        var results = await Task.WhenAll(endpoints.Select(source =>
            FetchAsync(source.Id, source.Manifest, source.Signature, cancellationToken))).ConfigureAwait(false);
        return results.OfType<VerifiedManifestSource>().ToArray();
    }

    public async Task<VerifiedManifestSource?> FetchAsync(string sourceId, Uri manifestUri, Uri signatureUri,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_sourceTimeout);
        try
        {
            var bytes = await FetchBytesAsync(manifestUri, UpdateManifestCodec.MaximumManifestSize, null, deadline.Token).ConfigureAwait(false);
            if (bytes is null) return null;
            var signature = await FetchBytesAsync(signatureUri, 1024, null, deadline.Token).ConfigureAwait(false);
            if (signature is null || !_verifier.Verify(bytes, signature)) return null;
            var manifest = UpdateManifestCodec.Parse(bytes);
            return manifest.KeyId == _keyId && manifest.Channel == _channel
                ? new VerifiedManifestSource(sourceId, manifest, bytes) { SignatureBytes = signature } : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException) { return null; }
    }

    public async Task<byte[]?> DownloadAssetAsync(UpdateAsset asset, CancellationToken cancellationToken = default)
    {
        UpdateManifestCodec.ValidateAsset(asset);
        cancellationToken.ThrowIfCancellationRequested();
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromMinutes(3));
        foreach (var mirror in asset.Mirrors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total.IsCancellationRequested) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            deadline.CancelAfter(_mirrorTimeout);
            try
            {
                var bytes = await FetchBytesAsync(new Uri(mirror.Url), asset.Size, asset.Size, deadline.Token).ConfigureAwait(false);
                if (bytes is not null && Convert.ToHexString(SHA256.HashData(bytes)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    return bytes;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (exception is HttpRequestException or IOException) { }
        }
        return null;
    }

    private async Task<byte[]?> FetchBytesAsync(Uri uri, long maximum, long? expected, CancellationToken token)
    {
        var visited = new HashSet<Uri>();
        for (var hop = 0; hop <= 5; hop++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsAllowedUri(uri, _allowedHosts) || !visited.Add(uri)) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            // The transport must not silently follow redirects, even to another allowed host.
            if (response.RequestMessage?.RequestUri != uri) return null;
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null || !Uri.TryCreate(uri, location, out var next)) return null;
                uri = next;
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0) return null;
            var length = response.Content.Headers.ContentLength;
            if (length is < 0 || length > maximum || expected.HasValue && length.HasValue && length != expected) return null;
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var result = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - result.Length + 1)), token).ConfigureAwait(false);
                if (read == 0) break;
                if (result.Length + read > maximum) return null;
                result.Write(buffer, 0, read);
            }
            token.ThrowIfCancellationRequested();
            if (length.HasValue && result.Length != length || expected.HasValue && result.Length != expected) return null;
            return result.ToArray();
        }
        return null;
    }

    public void Dispose() => _client.Dispose();
}
