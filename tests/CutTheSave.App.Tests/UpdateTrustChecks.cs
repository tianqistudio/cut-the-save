using System.IO;
using CutTheSave.App;

internal static class UpdateTrustChecks
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "update-trust.ini");
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var sources = new[]
        {
            new UpdateSourceConfiguration("a", new Uri("https://a.example/manifest"), new Uri("https://a.example/signature")),
            new UpdateSourceConfiguration("b", new Uri("https://b.example/manifest"), new Uri("https://b.example/signature"))
        };
        var pinned = new UpdateConfiguration(true, key, "stable", ["a.example", "b.example"], sources) { KeyId = "pinned-test-key" };
        Check(UpdateConfiguration.Load(path, pinned).IsReady, "compiled trust does not depend on an INI");
        File.WriteAllText(path, "[Update]\nEnabled=true\nPublicKeyBase64=ATTACKER\nKeyId=evil\nChannel=preview\nAllowedHosts=evil.example\nSource1Manifest=https://evil.example/manifest\nSource1Signature=https://evil.example/signature\n");
        var before = File.ReadAllBytes(path);
        var loaded = UpdateConfiguration.Load(path, pinned);
        Check(loaded.IsReady && loaded.PublicKeyBase64 == key && loaded.KeyId == pinned.KeyId && loaded.Channel == "stable" &&
            loaded.AllowedHosts.SequenceEqual(pinned.AllowedHosts) && loaded.Sources.SequenceEqual(sources), "INI cannot replace trust or endpoints");
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "loading does not rewrite user preferences");
        Check(!UpdateConfiguration.Load(path, UpdateTrustCatalog.Configuration).IsReady, "INI cannot provision this unconfigured production build");
        foreach (var content in new[] { "[Update]\nEnabled=false", "[Update]\nEnabled=maybe", "[Update]\nEnabled=false\nEnabled=true", new string('x', 8193) })
        {
            File.WriteAllText(path, content);
            Check(!UpdateConfiguration.Load(path, pinned).IsReady, "disabled, ambiguous or oversized preference stays offline");
        }
        foreach (var url in new[] { "http://a.example/manifest", "https://a.example:444/manifest", "https://user@a.example/manifest", "https://a.example/manifest#f", "https://evil.example/manifest" })
            Check(!(pinned with { Sources = [sources[0] with { ManifestUri = new Uri(url) }, sources[1]] }).IsReady, "unsafe compiled endpoint rejected");
        Check(!(pinned with { Sources = [sources[0], sources[0]] }).IsReady, "duplicate sources are not redundancy");
        Check(!(pinned with { Sources = [sources[0], sources[1] with { ManifestUri = new Uri("https://a.example/other") }] }).IsReady, "same host is not independent source");
        Check(!(pinned with { KeyId = null }).IsReady, "missing pinned key identity stays offline");
        Check(!(pinned with { AllowedHosts = ["*.example"] }).IsReady, "wildcard host is not approved");
        Console.WriteLine("PASS update trust is compiled; INI only disables/enables permitted checks and cannot redirect trust");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
