namespace CutTheSave.Core;

public sealed record ReleaseUpdateEndpoint(string Id, Uri ManifestUri, Uri SignatureUri);

// Shared by the main app and standalone updater. Provision only reviewed public
// trust in source; neither program accepts a key or source override from a job.
public static class ReleaseUpdateTrust
{
    public static bool Enabled => false;
    public static string? PublicKeyBase64 => null;
    public static string? KeyId => null;
    public static string Channel => "stable";
    public static IReadOnlyList<string> AllowedHosts => [];
    public static IReadOnlyList<ReleaseUpdateEndpoint> Sources => [];

    public static UpdatePackageTransaction CreateTransaction()
    {
        if (!Enabled || PublicKeyBase64 is null || KeyId is null)
            throw new InvalidOperationException("Production update trust is not provisioned.");
        return new(new Ed25519ManifestVerifier(Convert.FromBase64String(PublicKeyBase64)), KeyId, Channel);
    }
}
