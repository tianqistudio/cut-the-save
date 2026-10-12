using CutTheSave.Infrastructure.Windows;
using CutTheSave.Core;
using System.Security.Cryptography;

if (args.Length > 0 && args[0] == "--rtss-transaction-crash") return RtssTransactionTests.RunChild(args);
Assert(RefreshRatePolicy.GetAdvice(60) == RefreshRateAdvice.NoExtraLimitNeeded, "60 Hz must not need an extra limit");
Assert(RefreshRatePolicy.GetAdvice(0) == RefreshRateAdvice.Unknown, "unknown refresh rate must remain unknown");
Assert(!RefreshRatePolicy.CanApplyLimit(0, hasRtssHooks: true), "unknown refresh rate must not enable Apply");
Assert(RefreshRatePolicy.GetAdvice(59) == RefreshRateAdvice.Unknown, "non-positive policy boundary remains unknown below 60 Hz");
Assert(RefreshRatePolicy.CanApplyLimit(144, hasRtssHooks: true), "high refresh rate with hooks can apply");
Assert(!RefreshRatePolicy.CanApplyLimit(144, hasRtssHooks: false), "high refresh rate without hooks cannot apply");
Assert(SteamLocator.ParseInstallDirectoryNameFromText("\"installdir\"\t\"Cut the Rope Custom\"") == "Cut the Rope Custom", "Steam installdir is parsed");
Assert(SteamLocator.ParseInstallDirectoryNameFromText("\"installdir\"\t\"..\\Other\"") is null, "Steam installdir rejects path traversal");
Assert(SteamLocator.ParseInstallDirectoryNameFromText("\"appid\"\t\"223280\"") is null, "Steam installdir remains optional");
SteamLocatorTests.Run();
var overrideLocation = new SaveLocator().GetDefaultLocation("C:\\example\\ctr_save.bin");
Assert(overrideLocation.IsOverride && overrideLocation.Path.EndsWith("ctr_save.bin", StringComparison.OrdinalIgnoreCase), "save override path is honored");
RunIsolatedRtssProfileTest();
RunRtssRepairWorkflowTest();
RunRtssStateStoreFallbackTest();
RtssCompatibilityTests.Run();
RtssTransactionTests.Run();
var writeRealSave = args.Any(arg => string.Equals(arg, "--write-real", StringComparison.OrdinalIgnoreCase));

if (!writeRealSave && !args.Contains("--field-read-only")) { Console.WriteLine("PASS isolated infrastructure tests; real-device discovery skipped."); return 0; }

var steamCandidates = new SteamLocator().FindCutTheRopeInstallations();
Console.WriteLine($"STEAM_CANDIDATES {steamCandidates.Count} EXECUTABLES {steamCandidates.Count(item => File.Exists(item.ExecutablePath))}");
var saveLocator = new SaveLocator();
var defaultSave = saveLocator.GetDefaultLocation();
IEnumerable<string> initialSaveCandidates = defaultSave.Exists ? new[] { defaultSave.Path } : Array.Empty<string>();
var saveCandidates = initialSaveCandidates.Concat(saveLocator.FindNearbyCandidates()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
Console.WriteLine($"SAVE_CANDIDATES {saveCandidates.Length}");
if (writeRealSave && saveCandidates.Length != 1)
    throw new InvalidOperationException($"Refusing real save write because {saveCandidates.Length} candidates were found; expected exactly one.");

if (writeRealSave)
    Console.WriteLine("REAL_SAVE_WRITE_ENABLED explicit-user-authorization=True byte-identical-only=True");

var codec = new SaveCodec();
var validator = new SaveValidator();
foreach (var candidate in saveCandidates)
{
    try
    {
        var originalBytes = File.ReadAllBytes(candidate);
        using var stream = new MemoryStream(originalBytes, writable: false);
        var save = codec.Read(stream);
        var validation = validator.Validate(save);
        using var roundTrip = new MemoryStream();
        codec.Write(roundTrip, save);
        var roundTripMatches = originalBytes.AsSpan().SequenceEqual(roundTrip.ToArray());
        if (writeRealSave && !roundTripMatches)
        {
            Console.WriteLine("REAL_SAVE_WRITE_SKIPPED reason=roundtrip-diff");
            continue;
        }

        var scratch = Path.Combine(Path.GetTempPath(), $"CutTheSave-real-save-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var scratchTarget = Path.Combine(scratch, "ctr_save.bin");
            var snapshot = new AtomicSaveService(codec, validator).Save(scratchTarget, save, Path.Combine(scratch, "backups"));
            var safeWriteBytes = File.ReadAllBytes(snapshot.Path);
            using var safeWriteStream = new MemoryStream(safeWriteBytes, writable: false);
            var safeWriteSave = codec.Read(safeWriteStream);
            var safeWriteValid = validator.Validate(safeWriteSave).IsValid;
            Console.WriteLine($"REAL_SAVE_READ entries={save.IntEntries.Count + save.StringEntries.Count} valid={validation.IsValid} warnings={validation.HasWarnings} roundTrip={roundTripMatches} safeWrite={safeWriteValid}");

            if (writeRealSave)
            {
                var backupDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CutTheSave",
                    "backups");
                var backupService = new BackupService(backupDirectory, codec, validator);
                var preWriteSnapshot = backupService.CreateSnapshot(
                    candidate,
                    "real-write-preflight",
                    deduplicateByHash: false,
                    note: "Explicit user-authorized byte-identical real-save write acceptance.");
                if (preWriteSnapshot is null)
                    throw new IOException("Could not create the pre-write save backup.");

                var beforeHash = Convert.ToHexString(SHA256.HashData(originalBytes));
                _ = new AtomicSaveService(codec, validator).Save(candidate, save, backupDirectory);
                var afterBytes = File.ReadAllBytes(candidate);
                var afterHash = Convert.ToHexString(SHA256.HashData(afterBytes));
                if (!originalBytes.AsSpan().SequenceEqual(afterBytes))
                {
                    backupService.Restore(preWriteSnapshot.Path, candidate, "real-write-byte-mismatch-rollback");
                    throw new IOException("Real save write changed bytes; the pre-write backup was restored.");
                }

                using var postWriteStream = new MemoryStream(afterBytes, writable: false);
                var postWriteSave = codec.Read(postWriteStream);
                var postWriteValidation = validator.Validate(postWriteSave);
                if (!postWriteValidation.IsValid)
                    throw new InvalidDataException("Real save post-write validation failed.");

                Console.WriteLine($"REAL_SAVE_WRITE bytesIdentical=True beforeSha256={beforeHash} afterSha256={afterHash} backup={preWriteSnapshot.Path}");
            }
        }
        finally
        {
            if (Directory.Exists(scratch))
                if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(scratch, recursive: true);
        }
    }
    catch (Exception ex) when (ex is IOException or UnsupportedSaveException or InvalidDataException)
    {
        Console.WriteLine($"REAL_SAVE_READ_FAILED type={ex.GetType().Name}");
    }
}

var installations = new RtssDetector().FindInstallations();
if (installations.Count == 0)
{
    Console.WriteLine("SKIP RTSS not installed");
    return 0;
}

var selected = installations[0];
Console.WriteLine($"FOUND {selected.InstallPath}");
Console.WriteLine($"VERSION {selected.Version ?? "unknown"}");
Console.WriteLine($"FILES executable={selected.HasExecutable} hooks={selected.HasHooks}");

if (!selected.HasHooks)
{
    Console.WriteLine("SKIP RTSSHooks64.dll not present");
    return 0;
}

var state = new RtssProfileService().Read(selected);
Console.WriteLine($"READ profile={state.ProfileName} limit={state.FramerateLimit?.ToString() ?? "unset"} denominator={state.FramerateLimitDenominator?.ToString() ?? "unset"}");
return 0;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RunIsolatedRtssProfileTest()
{
    var root = Path.Combine(Path.GetTempPath(), $"CutTheSave-rtss-test-{Guid.NewGuid():N}");
    var profiles = Path.Combine(root, "Profiles");
    var hooks = Path.Combine(root, "RTSSHooks64.dll");
    var backupDirectory = Path.Combine(root, "backups");
    Directory.CreateDirectory(profiles);
    File.WriteAllText(hooks, "fake");

    try
    {
        var profileName = RtssProfileService.TargetProfile;
        var profilePath = Path.Combine(profiles, profileName + ".cfg");
        File.WriteAllText(profilePath, "FramerateLimit=30\nFramerateLimitDenominator=1\n");
        var installation = new RtssInstallation(root, "test", true, true, false, null, hooks);
        var backend = new FakeRtssBackend(root);
        var service = new RtssProfileService(_ => backend.CreateApi());

        var applied = service.Apply60Fps(installation, backupDirectory, profileName);
        Assert(applied.FramerateLimit == 60 && applied.FramerateLimitDenominator == 1, "isolated RTSS apply reads back 60/1");
        Assert(applied.BackupPath is not null && File.Exists(applied.BackupPath), "isolated RTSS apply creates a backup");
        Assert(File.ReadAllText(applied.BackupPath!).Contains("FramerateLimit=30", StringComparison.Ordinal), "isolated RTSS backup contains the previous limit");

        var restored = service.Restore(installation, applied.BackupPath!, profileName);
        Assert(restored.FramerateLimit == 30 && restored.FramerateLimitDenominator == 1, "isolated RTSS restore returns the previous limit");

        // Reuse the sole allowed game name in this owned synthetic directory.
        File.Delete(profilePath);
        var newProfileName = RtssProfileService.TargetProfile;
        backend.ThrowAfterSave = true;
        var failedWriteWasRejected = false;
        try
        {
            service.Apply60Fps(installation, backupDirectory, newProfileName);
        }
        catch (IOException)
        {
            failedWriteWasRejected = true;
        }
        Assert(failedWriteWasRejected, "isolated RTSS failure path is rejected");
        Assert(!File.Exists(Path.Combine(profiles, newProfileName + ".cfg")), "failed first-time RTSS write removes the created profile");

        backend.ThrowAfterSave = false;
        var undeletedProfileName = RtssProfileService.TargetProfile;
        var undeletedProfilePath = Path.Combine(profiles, undeletedProfileName + ".cfg");
        File.WriteAllText(undeletedProfilePath, "FramerateLimit=60\nFramerateLimitDenominator=1\n");
        backend.IgnoreDelete = true;
        var silentDeleteWasRejected = false;
        try
        {
            service.RemoveCreatedProfile(installation, undeletedProfileName);
        }
        catch (IOException)
        {
            silentDeleteWasRejected = true;
        }
        Assert(silentDeleteWasRejected, "silent RTSS delete failure is rejected");
        Assert(File.Exists(undeletedProfilePath), "silent RTSS delete failure is detected by file read-back");
        backend.IgnoreDelete = false;
        service.RemoveCreatedProfile(installation, undeletedProfileName);

        Console.WriteLine("PASS isolated RTSS apply, readback, backup, restore, failed-write cleanup, and delete verification");
    }
    finally
    {
        if (Directory.Exists(root))
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, recursive: true);
    }
}

static void RunRtssRepairWorkflowTest()
{
    var root = Path.Combine(Path.GetTempPath(), $"CutTheSave-rtss-workflow-test-{Guid.NewGuid():N}");
    var profiles = Path.Combine(root, "Profiles");
    var hooks = Path.Combine(root, "RTSSHooks64.dll");
    var backups = Path.Combine(root, "backups");
    var statePath = Path.Combine(root, "state", "rtss-state.json");
    Directory.CreateDirectory(profiles);
    File.WriteAllText(hooks, "fake");

    try
    {
        var profilePath = Path.Combine(profiles, RtssProfileService.TargetProfile + ".cfg");
        var originalProfile = "FramerateLimit=30\nFramerateLimitDenominator=1\n";
        File.WriteAllText(profilePath, originalProfile);
        var installation = new RtssInstallation(root, "test", true, true, false, null, hooks);
        var backend = new FakeRtssBackend(root);
        var service = new RtssProfileService(_ => backend.CreateApi());
        var store = new RtssStateStore(statePath);
        var workflow = new RtssRepairWorkflow(service, store, backups);

        var applied = workflow.Apply60Fps(installation);
        Assert(applied.FramerateLimit == 60 && applied.FramerateLimitDenominator == 1, "repair workflow applies 60/1");
        Assert(store.StateFileExists && store.Load() is not null, "repair workflow persists undo state before returning success");
        var duplicateApplyWasRejected = false;
        try
        {
            workflow.Apply60Fps(installation);
        }
        catch (InvalidOperationException)
        {
            duplicateApplyWasRejected = true;
        }
        Assert(duplicateApplyWasRejected, "active repair blocks a second apply");
        Assert(File.ReadAllText(profilePath).Contains("FramerateLimit=60", StringComparison.Ordinal), "blocked duplicate apply leaves the active profile unchanged");

        var restored = workflow.Undo(installation);
        Assert(!restored.RemovedCreatedProfile, "undo identifies a restored original profile");
        Assert(File.ReadAllText(profilePath) == originalProfile, "undo restores the exact original profile bytes");
        Assert(!store.StateFileExists, "undo clears persisted repair state only after success");

        File.Delete(profilePath);
        var created = workflow.Apply60Fps(installation);
        Assert(!created.HadOriginalProfile && File.Exists(profilePath), "workflow records a newly created profile");
        var removed = workflow.Undo(installation);
        Assert(removed.RemovedCreatedProfile && !File.Exists(profilePath), "undo removes a profile created by the workflow");

        var blockedStatePath = Path.Combine(root, "state-target-is-a-directory");
        Directory.CreateDirectory(blockedStatePath);
        var failingStore = new RtssStateStore(blockedStatePath);
        var failingWorkflow = new RtssRepairWorkflow(service, failingStore, backups);
        var stateFailureWasRejected = false;
        try
        {
            failingWorkflow.Apply60Fps(installation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stateFailureWasRejected = true;
        }
        Assert(stateFailureWasRejected, "repair workflow rejects success without an undo record");
        Assert(!File.Exists(profilePath), "failed intent persistence prevents creating a Profile");
        Assert(!Directory.EnumerateFiles(root, "state-target-is-a-directory.*.tmp").Any(), "failed state persistence removes its transaction temp file");

        Assert(
            RtssRepairStoragePaths.StatePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase),
            "default RTSS undo state uses the machine data directory required by the elevated helper");
        Console.WriteLine("PASS RTSS repair workflow is transactional for apply, state persistence, exact undo, and created-profile removal");
    }
    finally
    {
        if (Directory.Exists(root))
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, recursive: true);
    }
}

static void RunRtssStateStoreFallbackTest()
{
    var root = Path.Combine(Path.GetTempPath(), $"CutTheSave-rtss-state-test-{Guid.NewGuid():N}");
    var primaryPath = Path.Combine(root, "machine", "rtss-state.json");
    var fallbackPath = Path.Combine(root, "legacy", "rtss-state.json");
    try
    {
        var expected = new PersistedRtssRepairState("C:\\RTSS", RtssProfileService.TargetProfile, false, null, DateTimeOffset.UtcNow);
        new RtssStateStore(fallbackPath).Save(expected);
        var store = new RtssStateStore(primaryPath, fallbackPath);
        var loaded = store.Load();
        Assert(loaded == expected, "state store loads the legacy user-state fallback when machine state is absent");
        store.Save(expected with { HadOriginalProfile = true, BackupPath = "backup.cfg" });
        Assert(store.Load()?.HadOriginalProfile == true, "machine state takes precedence over the legacy fallback");
        store.Clear();
        Assert(!File.Exists(primaryPath) && !File.Exists(fallbackPath), "state clear removes primary and legacy records after verified undo");
        Console.WriteLine("PASS RTSS state store supports machine state and legacy-user fallback");
    }
    finally
    {
        if (Directory.Exists(root))
            if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") Directory.Delete(root, recursive: true);
    }
}

sealed class FakeRtssBackend
{
    private readonly string _profilesDirectory;

    public FakeRtssBackend(string installPath)
    {
        _profilesDirectory = Path.Combine(installPath, "Profiles");
    }

    public bool ThrowAfterSave { get; set; }
    public bool IgnoreDelete { get; set; }

    public FakeRtssApi CreateApi() => new(this, _profilesDirectory);

    public sealed class FakeRtssApi : IRtssProfileApi
    {
        private readonly FakeRtssBackend _backend;
        private readonly string _profilesDirectory;
        private string? _profileName;
        private int? _limit;
        private int? _denominator;

        public FakeRtssApi(FakeRtssBackend backend, string profilesDirectory)
        {
            _backend = backend;
            _profilesDirectory = profilesDirectory;
        }

        public void LoadProfile(string profileName)
        {
            _profileName = profileName;
            var path = Path.Combine(_profilesDirectory, profileName + ".cfg");
            _limit = null;
            _denominator = null;
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2 || !int.TryParse(parts[1], out var value))
                    continue;
                if (parts[0] == "FramerateLimit") _limit = value;
                if (parts[0] == "FramerateLimitDenominator") _denominator = value;
            }
        }

        public int? GetInt(string propertyName) => propertyName switch
        {
            "FramerateLimit" => _limit,
            "FramerateLimitDenominator" => _denominator,
            _ => null
        };

        public void SetInt(string propertyName, int value)
        {
            if (propertyName == "FramerateLimit") _limit = value;
            if (propertyName == "FramerateLimitDenominator") _denominator = value;
        }

        public void SaveProfile(string profileName)
        {
            Directory.CreateDirectory(_profilesDirectory);
            File.WriteAllText(Path.Combine(_profilesDirectory, profileName + ".cfg"), $"FramerateLimit={_limit}\nFramerateLimitDenominator={_denominator}\n");
            if (_backend.ThrowAfterSave)
                throw new IOException("simulated RTSS save failure");
        }

        public void DeleteProfile(string profileName)
        {
            if (_backend.IgnoreDelete)
                return;
            var path = Path.Combine(_profilesDirectory, profileName + ".cfg");
            if (File.Exists(path))
                File.Delete(path);
        }

        public void UpdateProfiles() { }
        public void Dispose() { }
    }
}
