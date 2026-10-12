using System.IO;
using CutTheSave.Core;

namespace CutTheSave.App;

public sealed record UpdateSourceConfiguration(string Id, Uri ManifestUri, Uri SignatureUri);

public sealed record UpdateConfiguration(bool Enabled, string? PublicKeyBase64, string Channel,
    IReadOnlyList<string> AllowedHosts, IReadOnlyList<UpdateSourceConfiguration> Sources)
{
    public string? KeyId { get; init; }
    public bool IsReady => ValidationError is null;

    public string? ValidationError
    {
        get
        {
            if (!Enabled) return "Signed updates are disabled.";
            if (!TryReadPublicKey(PublicKeyBase64) || string.IsNullOrWhiteSpace(KeyId))
                return "The compiled Ed25519 key and key ID have not been provisioned.";
            if (string.IsNullOrWhiteSpace(Channel) || AllowedHosts.Count == 0 ||
                AllowedHosts.Any(host => !UpdateSourceClient.IsValidHost(host)))
                return "The compiled channel or HTTPS allowlist is invalid.";
            if (Sources.Count != 2 || Sources.Select(source => source.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2 ||
                Sources.Select(source => source.ManifestUri).Distinct().Count() != 2 ||
                Sources.Select(source => source.SignatureUri).Distinct().Count() != 2 ||
                Sources.Select(source => source.ManifestUri.IsAbsoluteUri ? source.ManifestUri.IdnHost : "").Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
                return "Two independent signed update sources are required.";
            return Sources.All(source => !string.IsNullOrWhiteSpace(source.Id) &&
                source.ManifestUri != source.SignatureUri &&
                UpdateSourceClient.IsAllowedUri(source.ManifestUri, AllowedHosts) &&
                UpdateSourceClient.IsAllowedUri(source.SignatureUri, AllowedHosts))
                ? null : "Every compiled update URL must use an approved HTTPS host, port 443 and no credentials or fragment.";
        }
    }

    private static bool TryReadPublicKey(string? value)
    {
        try { return !string.IsNullOrWhiteSpace(value) && Convert.FromBase64String(value).Length == 32; }
        catch (FormatException) { return false; }
    }

    public static UpdateConfiguration Load() => Load(
        Path.Combine(AppContext.BaseDirectory, "CutTheSave.update.ini"), UpdateTrustCatalog.Configuration);

    // Only the on/off preference is external. Keys, channels, hosts and endpoints
    // are compiled trust, never values copied from an INI or downloaded manifest.
    internal static UpdateConfiguration Load(string path, UpdateConfiguration pinned)
    {
        try
        {
            if (!File.Exists(path)) return pinned;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 8192) return pinned with { Enabled = false };
            using var reader = new StreamReader(stream);
            var section = "";
            bool? enabled = null;
            while (reader.ReadLine() is { } raw)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;
                if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
                var separator = line.IndexOf('=');
                if (!section.Equals("Update", StringComparison.OrdinalIgnoreCase) || separator <= 0 ||
                    !line[..separator].Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase)) continue;
                if (enabled is not null || !bool.TryParse(line[(separator + 1)..].Trim(), out var value))
                    return pinned with { Enabled = false };
                enabled = value;
            }
            // A user preference cannot enable a build disabled by its publisher.
            return pinned with { Enabled = pinned.Enabled && (enabled ?? true) };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return pinned with { Enabled = false }; }
    }
}

internal static class UpdateTrustCatalog
{
    // Provision only the public key, its ID and reviewed project-owned URLs in
    // source before building a release. No production key/endpoints approved yet.
    // INI configuration, environment variables and downloaded data cannot fill in
    // these values. Private signing keys never belong in this application.
    internal static UpdateConfiguration Configuration => new(ReleaseUpdateTrust.Enabled,
        ReleaseUpdateTrust.PublicKeyBase64, ReleaseUpdateTrust.Channel, ReleaseUpdateTrust.AllowedHosts,
        ReleaseUpdateTrust.Sources.Select(source => new UpdateSourceConfiguration(source.Id, source.ManifestUri, source.SignatureUri)).ToArray())
        { KeyId = ReleaseUpdateTrust.KeyId };
}
