using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using static CutTheSave.Core.UpdatePackageFiles;

namespace CutTheSave.Core;

// No network, process launch or elevation. The production caller supplies only
// compiled trust; isolated tests inject their own ephemeral verifier.
public sealed class UpdatePackageTransaction(Ed25519ManifestVerifier verifier, string keyId, string channel)
{
    internal Action<string>? Checkpoint { get; init; }

    public PreparedUpdate Prepare(VerifiedManifestSource source, byte[] package, string targetDirectory,
        string cacheDirectory, string workerPath, int parentProcessId, long parentStartTimeUtcTicks,
        CancellationToken cancellationToken = default)
    {
        var manifest = VerifyManifest(source.ManifestBytes, source.SignatureBytes);
        var asset = manifest.Assets.Single(IsSupportedRuntime);
        if (!asset.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || package.LongLength != asset.Size ||
            !Hash(package).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The signed update ZIP size or hash does not match.");
        var target = Root(targetDirectory);
        var cache = Root(cacheDirectory);
        if (!Directory.Exists(target) || !File.Exists(Child(target, "CutTheSave.exe")) ||
            target.Equals(cache, StringComparison.OrdinalIgnoreCase) || IsInside(target, cache) || IsInside(cache, target))
            throw new IOException("The application directory and update cache must be existing/separate safe locations.");
        if (parentProcessId <= 0 || parentStartTimeUtcTicks <= 0) throw new ArgumentException("A parent process identity is required.");

        using var input = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var contents = InspectZip(archive, cancellationToken);
        VerifyAssemblyVersion(archive, manifest.Version);
        var changes = contents.Select(file => new UpdateFileChange(file.Path, Snapshot(Child(target, file.Path)), file.Version)).ToArray();
        if (changes.Sum(file => file.Before?.Size ?? 0) > MaximumExpandedSize) throw new IOException("The pre-update backup exceeds its size limit.");
        var workerVersion = Snapshot(workerPath) ?? throw new IOException("The standalone updater is missing.");
        using var worker = OpenVerified(workerPath, workerVersion);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(cache);
        NoLinks(cache);
        var jobId = Guid.NewGuid().ToString("N");
        var job = Child(cache, "update-" + jobId);
        Directory.CreateDirectory(job);
        var payload = Child(job, "payload");
        Directory.CreateDirectory(payload);
        // A failed preparation never touches the installation. Its job is kept
        // for inspection, not silently reused as a future update staging folder.
        WriteNew(Child(job, "package.zip"), package);
        foreach (var file in contents)
        {
            using var entry = file.Entry.Open();
            CopyNew(entry, Child(payload, file.Path), file.Version, cancellationToken);
        }
        var copiedWorker = Child(job, "CutTheSave.Updater.exe");
        CopyNew(worker, copiedWorker, workerVersion, cancellationToken);
        if (OperatingSystem.IsWindows())
            File.WriteAllText(Child(job, "package.zip") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var request = new UpdateInstallRequest(1, jobId, target, parentProcessId, parentStartTimeUtcTicks,
            Convert.ToBase64String(source.ManifestBytes), Convert.ToBase64String(source.SignatureBytes), changes);
        var requestPath = Child(job, "request.json");
        WriteJson(requestPath, request);
        var hash = Snapshot(requestPath)!.Sha256;
        WriteState(job, hash, "prepared");
        cancellationToken.ThrowIfCancellationRequested();
        return new(requestPath, hash, copiedWorker, workerVersion);
    }

    public UpdateInstallRequest ReadRequest(string requestPath, string requestHash)
    {
        if (requestHash is not { Length: 64 } || !requestHash.All(Uri.IsHexDigit)) throw new FormatException("Invalid request hash.");
        var path = Path.GetFullPath(requestPath);
        if (Path.GetFileName(path) != "request.json") throw new IOException("The updater requires an exact request file.");
        var request = ReadJson<UpdateInstallRequest>(path, 1024 * 1024, requestHash);
        if (request.SchemaVersion != 1 || !Guid.TryParseExact(request.JobId, "N", out _) ||
            Path.GetFileName(Path.GetDirectoryName(path)) != "update-" + request.JobId ||
            request.ParentProcessId <= 0 || request.ParentStartTimeUtcTicks <= 0 ||
            request.Files is not { Count: > 0 and <= MaximumFiles })
            throw new FormatException("Invalid update request identity or file inventory.");
        var target = Root(request.TargetDirectory);
        var job = Root(Path.GetDirectoryName(path)!);
        if (target != request.TargetDirectory || target.Equals(job, StringComparison.OrdinalIgnoreCase) || IsInside(target, job) || IsInside(job, target))
            throw new IOException("Update target overlaps the transaction directory.");
        _ = VerifyManifest(Convert.FromBase64String(request.ManifestBase64), Convert.FromBase64String(request.SignatureBase64));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in request.Files)
        {
            if (file is null) throw new FormatException("Null update file entry.");
            ValidateRelative(file.Path);
            if (!names.Add(file.Path)) throw new FormatException("Duplicate update file entry.");
            foreach (var version in new[] { file.Before, file.After }.OfType<UpdateFileVersion>())
                if (version.Size is < 0 or > MaximumFileSize || version.Sha256 is not { Length: 64 } ||
                    !version.Sha256.All(Uri.IsHexDigit) || version.Sha256 != version.Sha256.ToLowerInvariant())
                    throw new FormatException("Invalid update file version.");
            if (file.After is null) throw new FormatException("Missing update payload version.");
        }
        if (request.Files.Sum(file => file.After.Size) > MaximumExpandedSize || request.Files.Sum(file => file.Before?.Size ?? 0) > MaximumExpandedSize)
            throw new FormatException("Update file inventory exceeds its size limit.");
        return request;
    }

    public UpdateTransactionState ReadState(string requestPath, string requestHash)
    {
        var state = ReadJson<UpdateTransactionState>(Child(Root(Path.GetDirectoryName(Path.GetFullPath(requestPath))!), "state.json"), 8192);
        if (state.SchemaVersion != 1 || !string.Equals(state.RequestSha256, requestHash, StringComparison.OrdinalIgnoreCase) ||
            state.Status is not ("prepared" or "applying" or "committed" or "rolling-back" or "rolled-back" or "needs-review"))
            throw new FormatException("The update recovery record is invalid.");
        return state;
    }

    public UpdateTransactionState Install(string requestPath, string requestHash)
    {
        var request = ReadRequest(requestPath, requestHash);
        var job = Root(Path.GetDirectoryName(Path.GetFullPath(requestPath))!);
        using var gate = LockTarget(request.TargetDirectory);
        if (ReadState(requestPath, requestHash).Status != "prepared")
            throw new IOException("This update has already started. Review or explicitly recover its transaction first.");
        using var payloadLeases = ValidatePayload(job, request);
        foreach (var file in request.Files)
            if (!Matches(Child(request.TargetDirectory, file.Path), file.Before))
                throw new IOException("The installation changed after update confirmation; no files were replaced.");
        var backups = Child(job, "backups");
        Directory.CreateDirectory(backups);
        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            if (file.Before is null) continue;
            using var original = OpenVerified(Child(request.TargetDirectory, file.Path), file.Before);
            CopyNew(original, Child(backups, index + ".bin"), file.Before);
        }
        WriteState(job, requestHash, "applying");
        Checkpoint?.Invoke("after-intent");
        try
        {
            for (var index = 0; index < request.Files.Count; index++)
            {
                var file = request.Files[index];
                var target = Child(request.TargetDirectory, file.Path);
                if (!Matches(target, file.Before)) throw new IOException("An installation file changed during the update.");
                var temporary = Sidecar(target, request.JobId, index, "incoming");
                using (var staged = OpenVerified(Child(Child(job, "payload"), file.Path), file.After))
                    CopyNew(staged, temporary, file.After);
                NoLinks(target);
                if (file.Before is null) File.Move(temporary, target, overwrite: false);
                else
                {
                    var displaced = Sidecar(target, request.JobId, index, "previous");
                    if (File.Exists(displaced)) throw new IOException("Update rollback sidecar already exists.");
                    File.Replace(temporary, target, displaced);
                    if (!Matches(displaced, file.Before)) throw new IOException("Unexpected displaced bytes were retained for review.");
                }
                if (!Matches(target, file.After)) throw new IOException("Installed update read-back failed.");
                Checkpoint?.Invoke("after-file-" + index);
            }
            foreach (var file in request.Files)
                if (!Matches(Child(request.TargetDirectory, file.Path), file.After)) throw new IOException("Final installed payload changed.");
            WriteState(job, requestHash, "committed");
            Checkpoint?.Invoke("after-commit");
            return ReadState(requestPath, requestHash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return RollbackLocked(requestPath, requestHash, request, job, exception.Message);
        }
    }

    public UpdateTransactionState Rollback(string requestPath, string requestHash)
    {
        var request = ReadRequest(requestPath, requestHash);
        var job = Root(Path.GetDirectoryName(Path.GetFullPath(requestPath))!);
        using var gate = LockTarget(request.TargetDirectory);
        _ = ReadState(requestPath, requestHash);
        return RollbackLocked(requestPath, requestHash, request, job, "Explicit update recovery requested.");
    }

    private UpdateTransactionState RollbackLocked(string requestPath, string requestHash, UpdateInstallRequest request, string job, string detail)
    {
        try
        {
            // Check every current/prior version before changing any recovery target.
            for (var index = 0; index < request.Files.Count; index++)
            {
                var file = request.Files[index];
                var target = Child(request.TargetDirectory, file.Path);
                var current = Snapshot(target);
                if (current != file.Before && current != file.After) throw new IOException("External changes prevent automatic update recovery.");
                var displaced = Sidecar(target, request.JobId, index, "previous");
                if (File.Exists(displaced) && !Matches(displaced, file.Before)) throw new IOException("Unexpected displaced file retained; manual review required.");
                if (current == file.After && file.Before is not null)
                    using (OpenVerified(Child(Child(job, "backups"), index + ".bin"), file.Before)) { }
            }
            WriteState(job, requestHash, "rolling-back", detail);
            Checkpoint?.Invoke("rollback-intent");
            for (var index = request.Files.Count - 1; index >= 0; index--)
            {
                var file = request.Files[index];
                var target = Child(request.TargetDirectory, file.Path);
                if (Matches(target, file.Before)) continue;
                if (!Matches(target, file.After)) throw new IOException("An update recovery target changed.");
                var retained = Sidecar(target, request.JobId, index, "rolled-back-" + Guid.NewGuid().ToString("N"));
                if (file.Before is null)
                {
                    File.Move(target, retained, overwrite: false);
                    if (!Matches(retained, file.After)) throw new IOException("Unexpected recovery bytes retained for review.");
                }
                else
                {
                    var temporary = Sidecar(target, request.JobId, index, "restore-" + Guid.NewGuid().ToString("N"));
                    using (var original = OpenVerified(Child(Child(job, "backups"), index + ".bin"), file.Before))
                        CopyNew(original, temporary, file.Before);
                    File.Replace(temporary, target, retained);
                    if (!Matches(retained, file.After)) throw new IOException("Unexpected replaced recovery bytes retained for review.");
                }
                if (!Matches(target, file.Before)) throw new IOException("Restored file read-back failed.");
                Checkpoint?.Invoke("after-rollback-" + index);
            }
            foreach (var file in request.Files)
                if (!Matches(Child(request.TargetDirectory, file.Path), file.Before)) throw new IOException("Restored installation changed during final verification.");
            WriteState(job, requestHash, "rolled-back", detail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WriteState(job, requestHash, "needs-review", exception.Message);
        }
        return ReadState(requestPath, requestHash);
    }

    private UpdateManifest VerifyManifest(byte[] bytes, byte[] signature)
    {
        if (bytes.Length > UpdateManifestCodec.MaximumManifestSize || signature.Length > 1024 || !verifier.Verify(bytes, signature))
            throw new IOException("Update signature verification failed.");
        var manifest = UpdateManifestCodec.Parse(bytes);
        if (manifest.KeyId != keyId || manifest.Channel != channel ||
            manifest.BlockedVersions.Select(UpdateManifestCodec.NumericVersion).Contains(UpdateManifestCodec.NumericVersion(manifest.Version)) ||
            manifest.Assets.Count(IsSupportedRuntime) != 1)
            throw new IOException("The signed update does not match the compiled key/channel/runtime policy.");
        return manifest;
    }

    private LeaseSet ValidatePayload(string job, UpdateInstallRequest request)
    {
        var manifest = VerifyManifest(Convert.FromBase64String(request.ManifestBase64), Convert.FromBase64String(request.SignatureBase64));
        var asset = manifest.Assets.Single(IsSupportedRuntime);
        var leases = new LeaseSet();
        try
        {
            var package = OpenVerified(Child(job, "package.zip"), new(asset.Size, asset.Sha256));
            leases.Streams.Add(package);
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var contents = InspectZip(archive, CancellationToken.None);
            VerifyAssemblyVersion(archive, manifest.Version);
            if (contents.Count != request.Files.Count) throw new IOException("Signed payload inventory changed.");
            for (var index = 0; index < contents.Count; index++)
            {
                var content = contents[index];
                var file = request.Files[index];
                if (content.Path != file.Path || content.Version != file.After) throw new IOException("Payload inventory is not bound to the signed ZIP.");
                leases.Streams.Add(OpenVerified(Child(Child(job, "payload"), file.Path), file.After));
            }
            return leases;
        }
        catch { leases.Dispose(); throw; }
    }

    // Match the signed-metadata validator, update decision and download selection.
    private static bool IsSupportedRuntime(UpdateAsset asset) =>
        string.Equals(asset.Rid, "win-x64", StringComparison.OrdinalIgnoreCase);

    private static string Sidecar(string target, string jobId, int index, string role)
    {
        var path = Path.Combine(Path.GetDirectoryName(target)!, ".cts-update-" + jobId + "-" + index + "-" + role);
        NoLinks(path);
        return path;
    }
    private static void VerifyAssemblyVersion(ZipArchive archive, string signedVersion)
    {
        using var entry = archive.GetEntry("CutTheSave.dll")!.Open();
        using var bytes = new MemoryStream();
        entry.CopyTo(bytes);
        bytes.Position = 0;
        try
        {
            using var pe = new PEReader(bytes);
            var actual = pe.GetMetadataReader().GetAssemblyDefinition().Version;
            if (UpdateManifestCodec.NumericVersion(actual.ToString()) != UpdateManifestCodec.NumericVersion(signedVersion))
                throw new IOException("The packaged assembly version does not match the signed release version.");
        }
        catch (BadImageFormatException exception) { throw new IOException("The update assembly metadata is invalid.", exception); }
    }
    private static void WriteState(string job, string hash, string status, string? detail = null) =>
        WriteJson(Child(job, "state.json"), new UpdateTransactionState(1, hash, status, detail));

    private static TargetGate LockTarget(string target) => new(target);
    private sealed class TargetGate : IDisposable
    {
        private readonly Mutex _mutex;
        public TargetGate(string target)
        {
            _mutex = new Mutex(false, "Local\\CutTheSave.Update." + Hash(Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
            try { if (!_mutex.WaitOne(0)) throw new IOException("Another update or recovery is using this installation."); }
            catch (AbandonedMutexException) { }
            catch { _mutex.Dispose(); throw; }
        }
        public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
    }
    private sealed class LeaseSet : IDisposable
    {
        public List<FileStream> Streams { get; } = [];
        public void Dispose() { foreach (var stream in Streams) stream.Dispose(); }
    }
}
