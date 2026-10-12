using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NSec.Cryptography;

namespace CutTheSave.Core;

public sealed record UpdateMirror(string Id, string Url);

public sealed record UpdateAsset(
    string Rid,
    string FileName,
    long Size,
    string Sha256,
    IReadOnlyList<UpdateMirror> Mirrors);

public sealed record UpdateManifest(
    int SchemaVersion,
    string KeyId,
    string Channel,
    string Version,
    DateTimeOffset PublishedAt,
    string RecommendedVersion,
    IReadOnlyList<string> BlockedVersions,
    IReadOnlyList<UpdateAsset> Assets);

public sealed record VerifiedManifestSource(string SourceId, UpdateManifest Manifest, byte[] ManifestBytes)
{
    public byte[] SignatureBytes { get; init; } = [];
}

public enum UpdateDecisionKind
{
    NoValidSource,
    Available,
    AlreadyCurrent,
    SecurityConflict
}

public sealed record UpdateDecision(UpdateDecisionKind Kind, UpdateManifest? Manifest, string? Message);

public static class UpdateManifestCodec
{
    public const int MaximumManifestSize = 65536;
    public const long MaximumAssetSize = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12
    };

    public static UpdateManifest Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > MaximumManifestSize) throw new FormatException("Update metadata exceeds its size limit.");
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
            RejectDuplicates(json.RootElement);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, Options)
                ?? throw new FormatException("The update manifest is empty.");
            Validate(manifest);
            return manifest;
        }
        catch (JsonException exception) { throw new FormatException("The update manifest is malformed.", exception); }
    }

    internal static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("Duplicate update metadata property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicates(value);
    }

    internal static Version NumericVersion(string? text)
    {
        if (text is null || text.Length > 48 ||
            !Regex.IsMatch(text, @"^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$", RegexOptions.CultureInvariant) ||
            !Version.TryParse(text, out var version))
            throw new FormatException("An update version must have three or four numeric components.");
        return new Version(version.Major, version.Minor, version.Build, Math.Max(version.Revision, 0));
    }

    internal static void Validate(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !Token(manifest.KeyId) || !Token(manifest.Channel) ||
            manifest.PublishedAt == default || manifest.BlockedVersions is null || manifest.BlockedVersions.Count > 128 ||
            manifest.Assets is not { Count: > 0 and <= 8 })
            throw new FormatException("The update manifest is missing required fields or exceeds its bounds.");
        _ = NumericVersion(manifest.Version);
        _ = NumericVersion(manifest.RecommendedVersion);
        foreach (var version in manifest.BlockedVersions) _ = NumericVersion(version);
        if (manifest.Assets.Any(asset => asset is null) ||
            manifest.Assets.Select(asset => asset.Rid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Assets.Count)
            throw new FormatException("Each update runtime must have exactly one asset.");
        foreach (var asset in manifest.Assets) ValidateAsset(asset);
    }

    internal static void ValidateAsset(UpdateAsset asset)
    {
        if (!Token(asset.Rid) || asset.Size is <= 0 or > MaximumAssetSize ||
            asset.Sha256 is not { Length: 64 } || !asset.Sha256.All(Uri.IsHexDigit) ||
            asset.Mirrors is not { Count: > 0 and <= 8 })
            throw new FormatException("The update asset has invalid runtime, size, hash or mirrors.");
        if (asset.FileName is not { Length: > 0 and <= 128 } ||
            !Regex.IsMatch(asset.FileName, @"^[A-Za-z0-9][A-Za-z0-9_.()-]*\.(zip|exe)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(asset.FileName, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new FormatException("The update asset file name is unsafe.");
        if (asset.Mirrors.Any(mirror => mirror is null || !Token(mirror.Id) ||
            !Uri.TryCreate(mirror.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) ||
            asset.Mirrors.Select(mirror => mirror.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != asset.Mirrors.Count)
            throw new FormatException("The update asset contains invalid or duplicate mirrors.");
    }

    private static bool Token(string? text) => text is { Length: > 0 and <= 64 } &&
        text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static byte[] Serialize(UpdateManifest manifest)
    {
        Validate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
        if (bytes.Length > MaximumManifestSize) throw new FormatException("Update metadata exceeds its size limit.");
        return bytes;
    }
}

public sealed class Ed25519ManifestVerifier
{
    private readonly byte[] _publicKey;

    public Ed25519ManifestVerifier(ReadOnlySpan<byte> rawPublicKey)
    {
        if (rawPublicKey.Length != SignatureAlgorithm.Ed25519.PublicKeySize)
            throw new ArgumentException("An Ed25519 public key must be 32 bytes.", nameof(rawPublicKey));
        _publicKey = rawPublicKey.ToArray();
    }

    public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        var normalizedSignature = NormalizeSignature(signatureBytes);
        if (normalizedSignature is null)
            return false;
        try
        {
            var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, _publicKey, KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(publicKey, manifestBytes, normalizedSignature);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException)
        {
            return false;
        }
    }

    private static byte[]? NormalizeSignature(ReadOnlySpan<byte> signatureBytes)
    {
        if (signatureBytes.Length == SignatureAlgorithm.Ed25519.SignatureSize)
            return signatureBytes.ToArray();

        try
        {
            var text = Encoding.UTF8.GetString(signatureBytes).Trim();
            var decoded = Convert.FromBase64String(text);
            return decoded.Length == SignatureAlgorithm.Ed25519.SignatureSize ? decoded : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public sealed class UpdateDecisionService
{
    public UpdateDecision Choose(string currentVersion, IReadOnlyList<VerifiedManifestSource> sources, string rid = "win-x64")
    {
        ArgumentNullException.ThrowIfNull(sources);
        var current = UpdateManifestCodec.NumericVersion(currentVersion);
        foreach (var source in sources) UpdateManifestCodec.Validate(source.Manifest);

        // Normalize 1.0.0 and 1.0.0.0 to the same version. Compare BEFORE applying
        // blocklists so a blocked source cannot hide a signed same-version conflict.
        var candidates = sources.Where(source => source.Manifest.Assets.Any(asset =>
            string.Equals(asset.Rid, rid, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var group in candidates.GroupBy(source => UpdateManifestCodec.NumericVersion(source.Manifest.Version)))
        {
            var identities = group.Select(source => source.Manifest.Assets.Single(asset =>
                    string.Equals(asset.Rid, rid, StringComparison.OrdinalIgnoreCase)))
                .Select(asset => (asset.FileName, asset.Size, Hash: asset.Sha256.ToUpperInvariant())).Distinct().ToArray();
            if (identities.Length > 1)
                return new UpdateDecision(UpdateDecisionKind.SecurityConflict, null, $"Sources disagree about asset identity for version {group.Key}.");
        }

        // Honor a signed block from either source, including a newer source that
        // withdraws a version still advertised by a stale mirror.
        var blocked = sources.SelectMany(source => source.Manifest.BlockedVersions)
            .Select(UpdateManifestCodec.NumericVersion).ToHashSet();
        var chosen = candidates.Where(source => !blocked.Contains(UpdateManifestCodec.NumericVersion(source.Manifest.Version)))
            .OrderByDescending(source => UpdateManifestCodec.NumericVersion(source.Manifest.Version)).FirstOrDefault();
        if (chosen is null)
            return new UpdateDecision(UpdateDecisionKind.NoValidSource, null, "No valid signed source contains an unblocked runtime asset.");
        return new UpdateDecision(UpdateManifestCodec.NumericVersion(chosen.Manifest.Version) > current
            ? UpdateDecisionKind.Available : UpdateDecisionKind.AlreadyCurrent, chosen.Manifest, null);
    }
}
