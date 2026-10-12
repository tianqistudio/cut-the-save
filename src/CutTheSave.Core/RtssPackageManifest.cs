using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CutTheSave.Core;

// Metadata must come from the reviewed, compiled catalog (or a future pinned-key
// signed catalog). Parsing JSON alone never establishes publisher trust.
public sealed record RtssPackageManifest(int SchemaVersion, string Version, string FileName,
    long Size, string Sha256, string OfficialUrl, string InstallerFileName,
    long InstallerSize, string InstallerSha256)
{
    public const long MaximumPackageSize = 64 * 1024 * 1024;
    public const long MaximumInstallerSize = 128 * 1024 * 1024;
    public const string OfficialPage = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/";

    // Exact hosts, never suffix matching. Add a CDN only after verifying the
    // official page's actual redirect chain; a manifest cannot expand this list.
    public static bool IsAllowedSource(Uri uri) => uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.IdnHost.Equals("www.guru3d.com", StringComparison.OrdinalIgnoreCase) ||
         uri.IdnHost.Equals("guru3d.com", StringComparison.OrdinalIgnoreCase));

    public static RtssPackageManifest Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 65536) throw new FormatException("RTSS metadata exceeds the allowed size.");
        try
        {
            using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("RTSS metadata must be an object.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in json.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new FormatException("Duplicate RTSS metadata property.");
            var manifest = JsonSerializer.Deserialize<RtssPackageManifest>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                MaxDepth = 8
            }) ?? throw new FormatException("Empty RTSS metadata.");
            manifest.Validate();
            return manifest;
        }
        catch (JsonException exception) { throw new FormatException("Invalid RTSS metadata.", exception); }
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || !System.Version.TryParse(Version, out var version) || version.Build < 0 ||
            !Regex.IsMatch(Version, @"^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$", RegexOptions.CultureInvariant))
            throw new FormatException("Invalid RTSS version or schema.");
        if (!SafeName(FileName) || !SafeName(InstallerFileName) ||
            !InstallerFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Invalid RTSS package file name.");
        if (Size is <= 0 or > MaximumPackageSize || InstallerSize is <= 0 or > MaximumInstallerSize ||
            !Hash(Sha256) || !Hash(InstallerSha256)) throw new FormatException("Invalid RTSS size or hash.");
        if (!Uri.TryCreate(OfficialUrl, UriKind.Absolute, out var uri) || !IsAllowedSource(uri))
            throw new FormatException("RTSS source is not an approved HTTPS address.");
        if (FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (FileName != InstallerFileName || Size != InstallerSize ||
                !Sha256.Equals(InstallerSha256, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Direct installer metadata disagrees with package metadata.");
        }
        else if (!FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only reviewed ZIP or EXE packages are supported.");
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool SafeName(string? value) => value is { Length: > 0 and <= 128 } &&
        Regex.IsMatch(value, @"^[A-Za-z0-9\[][A-Za-z0-9 _\[\].()-]*\.(zip|exe)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        !Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
