using System.Text;
using System.Net;
using System.Net.Http;
using CutTheSave.Core;
using NSec.Cryptography;

if (args.Length > 0 && args[0] == "--update-transaction-crash") return UpdateTransactionTests.RunChild(args);
if (args.Length > 0 && args[0] == "--update-transactions") { UpdateTransactionTests.Run(); return 0; }

if (args.Length > 0 && args[0] == "--retention-crash") return BackupRecoveryTests.RunChild(args);
if (args.Length > 0 && args[0] == "--save-crash") return SaveRestartTests.RunChild(args);

var tests = new (string Name, Action Run)[]
{
    ("Signed ZIP installation, interruption and rollback", UpdateTransactionTests.Run),
    ("Score hash golden fixture", ScoreHashGoldenFixture),
    ("RTSS package metadata and official-download safety", RtssPackageTests.Run),
    ("Pinned signed update sources, bounded transport and failure isolation", UpdateSecurityTests.Run),
    ("Save round-trip preserves entry order", SaveRoundTrip),
    ("Codec rejects trailing data", CodecRejectsTrailingData),
    ("Codec rejects oversized counts", CodecRejectsOversizedCounts),
    ("Validator reports ranges and duplicates", ValidatorReportsIssues),
    ("Atomic save produces a validated file", AtomicSaveProducesValidatedFile),
    ("Backup snapshot deduplicates and restores", BackupSnapshotDeduplicatesAndRestores),
    ("Indexed backup history, locks, retention and failure safety", BackupSafetyTests.Run),
    ("Backup retention process-exit recovery", () => BackupRecoveryTests.Run()),
    ("Interrupted save discovery and explicit recovery", SaveRestartTests.Run),
    ("Completion operation is scoped", CompletionOperationIsScoped),
    ("Signed update manifest verification and conflict rules", SignedUpdateManifestRules),
    ("Malformed update manifest fields are rejected safely", MalformedUpdateManifestFields),
    ("Blocked update versions are excluded", BlockedUpdateVersionsAreExcluded),
    ("Update mirror allowlist and asset hash", UpdateMirrorSecurityRules),
    ("Level progress batch operations are scoped", LevelProgressBatchOperations),
    ("Indexed level projection and batch preservation", LevelProgressTests.Run)
};

var failures = new List<string>();
var total = 0;
foreach (var test in tests.Concat(WriteSafetyTests.Cases()))
{
    try
    {
        total++;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        test.Run();
        Console.WriteLine($"PASS {test.Name} ({clock.ElapsedMilliseconds} ms)");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception}");
        Console.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"RESULT executed={total} passed={total-failures.Count} failed={failures.Count}");
return failures.Count == 0 ? 0 : 1;

static void ScoreHashGoldenFixture()
{
    var actual = new ScoreHashService().Compute(9_700);
    AssertEqual("9c68e53f6057dfba5e0556c9a1e58e54\0", actual);
}

static void SaveRoundTrip()
{
    var source = new CutTheRopeSave();
    source.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 123));
    source.IntEntries.Add(new SaveIntEntry("UNLOCKED_0_0", 1));
    source.StringEntries.Add(new SaveStringEntry("PREFS_SCORE_HASH", "hash\0"));

    var codec = new SaveCodec();
    using var stream = new MemoryStream();
    codec.Write(stream, source);
    stream.Position = 0;
    var result = codec.Read(stream);

    Assert(source.IntEntries.SequenceEqual(result.IntEntries));
    Assert(source.StringEntries.SequenceEqual(result.StringEntries));
}

static void CodecRejectsTrailingData()
{
    var codec = new SaveCodec();
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0xFF);
    }

    stream.Position = 0;
    AssertThrows<UnsupportedSaveException>(() => codec.Read(stream));
}

static void CodecRejectsOversizedCounts()
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(5_001);
    }

    stream.Position = 0;
    AssertThrows<UnsupportedSaveException>(() => new SaveCodec().Read(stream));
}

static void ValidatorReportsIssues()
{
    var save = new CutTheRopeSave();
    save.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 6_000));
    save.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 1));
    save.IntEntries.Add(new SaveIntEntry("STARS_0_0", 4));

    var result = new SaveValidator().Validate(save);
    Assert(result.Issues.Any(issue => issue.Code == "SCORE-RANGE"));
    Assert(result.Issues.Any(issue => issue.Code == "STARS-RANGE"));
    Assert(result.Issues.Any(issue => issue.Code == "INT-DUPLICATE"));
    Assert(!result.IsValid);
}

static void AtomicSaveProducesValidatedFile()
{
    var root = Path.Combine(Path.GetTempPath(), "CutTheSave.Tests", Guid.NewGuid().ToString("N"));
    var target = Path.Combine(root, "ctr_save.bin");
    var backups = Path.Combine(root, "backups");
    Directory.CreateDirectory(root);

    try
    {
        var save = new CutTheRopeSave();
        save.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 42));
        save.SetString("PREFS_SCORE_HASH", new ScoreHashService().Compute(42));

        var snapshot = new AtomicSaveService().Save(target, save, backups);
        Assert(File.Exists(target));
        AssertEqual(new FileInfo(target).Length, snapshot.Length);
        Assert(Directory.Exists(backups));
    }
    finally
    {
        if (Directory.Exists(root))
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, recursive: true);
    }
}

static void BackupSnapshotDeduplicatesAndRestores()
{
    var root = Path.Combine(Path.GetTempPath(), "CutTheSave.Backup.Tests", Guid.NewGuid().ToString("N"));
    var target = Path.Combine(root, "ctr_save.bin");
    Directory.CreateDirectory(root);
    try
    {
        var save = new CutTheRopeSave();
        save.IntEntries.Add(new SaveIntEntry("SCORE_0_0", 10));
        using (var stream = File.Create(target))
            new SaveCodec().Write(stream, save);

        var service = new BackupService(Path.Combine(root, "backups"));
        var first = service.CreateSnapshot(target, "open", deduplicateByHash: true)!;
        var second = service.CreateSnapshot(target, "open", deduplicateByHash: true)!;
        AssertEqual(first.Path, second.Path);
        File.WriteAllBytes(target, [1, 2, 3]);
        service.Restore(first.Path, target);
        Assert(File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(first.Path)));

        var invalidBackup = Path.Combine(root, "backups", "invalid.bin");
        File.WriteAllBytes(invalidBackup, [9, 8, 7]);
        var beforeInvalidRestore = File.ReadAllBytes(target);
        var rejected = false;
        try
        {
            service.Restore(invalidBackup, target);
        }
        catch (UnsupportedSaveException)
        {
            rejected = true;
        }
        Assert(rejected);
        Assert(File.ReadAllBytes(target).SequenceEqual(beforeInvalidRestore));
    }
    finally
    {
        if (Directory.Exists(root))
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, recursive: true);
    }
}

static void CompletionOperationIsScoped()
{
    var save = new CutTheRopeSave();
    save.SetInt("PREFS_LEVELS_WON", 1);
    save.SetInt("PREFS_LAST_PACK", 0);
    save.SetInt("SCORE_0_0", 42);
    var changed = new LevelProgressService().SetAllCompleted(save);
    AssertEqual(2, changed);
    AssertEqual(275, save.IntEntries.First(entry => entry.Key == "PREFS_LEVELS_WON").Value);
    AssertEqual(10, save.IntEntries.First(entry => entry.Key == "PREFS_LAST_PACK").Value);
    AssertEqual(42, save.IntEntries.First(entry => entry.Key == "SCORE_0_0").Value);
}

static void SignedUpdateManifestRules()
{
    var asset = new UpdateAsset("win-x64", "CutTheSave.zip", 123, new string('a', 64), [new UpdateMirror("github", "https://example.invalid/release.zip")]);
    var manifest = new UpdateManifest(1, "test-key", "stable", "1.0.0", DateTimeOffset.UtcNow, "1.0.0", [], [asset]);
    var bytes = UpdateManifestCodec.Serialize(manifest);
    using var key = Key.Create(SignatureAlgorithm.Ed25519);
    var signature = SignatureAlgorithm.Ed25519.Sign(key, bytes);
    var verifier = new Ed25519ManifestVerifier(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    Assert(verifier.Verify(bytes, signature));
    Assert(verifier.Verify(bytes, Encoding.UTF8.GetBytes(Convert.ToBase64String(signature))));
    bytes[0] ^= 0x01;
    Assert(!verifier.Verify(bytes, signature));

    var sourceA = new VerifiedManifestSource("a", manifest, UpdateManifestCodec.Serialize(manifest));
    var conflicting = manifest with { Assets = [asset with { Sha256 = new string('b', 64) }] };
    var sourceB = new VerifiedManifestSource("b", conflicting, UpdateManifestCodec.Serialize(conflicting));
    var decision = new UpdateDecisionService().Choose("0.9.0", [sourceA, sourceB]);
    AssertEqual(UpdateDecisionKind.SecurityConflict, decision.Kind);
}

static void MalformedUpdateManifestFields()
{
    var malformed = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"keyId\":\"key\",\"channel\":\"stable\",\"version\":\"1.0.0\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"recommendedVersion\":\"1.0.0\",\"blockedVersions\":[],\"assets\":[{\"rid\":\"win-x64\",\"fileName\":\"x.zip\",\"size\":1,\"sha256\":null,\"mirrors\":null}]}");
    try
    {
        _ = UpdateManifestCodec.Parse(malformed);
        throw new InvalidOperationException("Malformed manifest was accepted.");
    }
    catch (FormatException)
    {
        // Expected: malformed remote input is rejected as data, not as an application crash.
    }
}

static void UpdateMirrorSecurityRules()
{
    var payload = Encoding.UTF8.GetBytes("signed release bytes");
    using var key = Key.Create(SignatureAlgorithm.Ed25519);
    using var client = new UpdateSourceClient(new FixedResponseHandler(payload), new Ed25519ManifestVerifier(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)), ["updates.example"], "test-key", "stable");
    var good = new UpdateAsset("win-x64", "release.zip", payload.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(), [new UpdateMirror("trusted", "https://updates.example/release.zip")]);
    var downloaded = client.DownloadAssetAsync(good).GetAwaiter().GetResult();
    Assert(downloaded is not null && downloaded.SequenceEqual(payload));

    var disallowed = good with { Mirrors = [new UpdateMirror("evil", "https://evil.example/release.zip")] };
    Assert(client.DownloadAssetAsync(disallowed).GetAwaiter().GetResult() is null);
    var wrongHash = good with { Sha256 = new string('0', 64) };
    Assert(client.DownloadAssetAsync(wrongHash).GetAwaiter().GetResult() is null);
}

static void BlockedUpdateVersionsAreExcluded()
{
    var asset = new UpdateAsset("win-x64", "CutTheSave.zip", 1, new string('a', 64), [new UpdateMirror("trusted", "https://example.invalid/release.zip")]);
    var blocked = new UpdateManifest(1, "test-key", "stable", "2.0.0", DateTimeOffset.UtcNow, "2.0.0", ["2.0.0"], [asset]);
    var decision = new UpdateDecisionService().Choose("1.0.0", [new VerifiedManifestSource("blocked", blocked, UpdateManifestCodec.Serialize(blocked))]);
    AssertEqual(UpdateDecisionKind.NoValidSource, decision.Kind);
}

static void LevelProgressBatchOperations()
{
    var save = new CutTheRopeSave();
    for (var box = 0; box < LevelProgressService.BoxCount; box++)
    {
        for (var level = 0; level < LevelProgressService.LevelsPerBox; level++)
        {
            save.SetInt($"SCORE_{box}_{level}", box == 0 && level == 0 ? 100 : 0);
            save.SetInt($"STARS_{box}_{level}", box == 0 && level == 0 ? 1 : 0);
            save.SetInt($"UNLOCKED_{box}_{level}", 0);
        }
    }

    var service = new LevelProgressService();
    AssertEqual(275, service.Project(save).Count);
    AssertEqual(275, service.SetAllUnlocked(save));
    AssertEqual(275, service.SetAllStars(save));
    AssertEqual(275, service.SetAllScores(save));
    AssertEqual(5_999, save.IntEntries.First(entry => entry.Key == "SCORE_0_0").Value);
    AssertEqual(3, save.IntEntries.First(entry => entry.Key == "STARS_0_0").Value);
    AssertEqual(1, save.IntEntries.First(entry => entry.Key == "UNLOCKED_0_0").Value);
    Assert(save.TryGetString("PREFS_SCORE_HASH", out _));

    var sparse = new CutTheRopeSave();
    sparse.SetInt("SCORE_0_0", 10);
    AssertEqual(1, service.SetAllScores(sparse));
    AssertEqual(1, sparse.IntEntries.Count);
}

static void Assert(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Assertion failed.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class FixedResponseHandler(byte[] payload) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
            RequestMessage = request
        };
        return Task.FromResult(response);
    }
}
