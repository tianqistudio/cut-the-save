using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CutTheSave.Core;

public sealed record UpdateFileVersion(long Size, string Sha256);
public sealed record UpdateFileChange(string Path, UpdateFileVersion? Before, UpdateFileVersion After);
public sealed record UpdateInstallRequest(int SchemaVersion, string JobId, string TargetDirectory,
    int ParentProcessId, long ParentStartTimeUtcTicks, string ManifestBase64, string SignatureBase64,
    IReadOnlyList<UpdateFileChange> Files);
public sealed record UpdateTransactionState(int SchemaVersion, string RequestSha256, string Status, string? Detail);
public sealed record PreparedUpdate(string RequestPath, string RequestSha256, string WorkerPath, UpdateFileVersion WorkerVersion)
{
    public FileStream OpenWorkerLease() => UpdatePackageFiles.OpenVerified(WorkerPath, WorkerVersion);
}

internal static class UpdatePackageFiles
{
    internal const long MaximumExpandedSize = 512L * 1024 * 1024;
    internal const long MaximumFileSize = 192L * 1024 * 1024;
    internal const int MaximumFiles = 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12, WriteIndented = true
    };

    internal static T ReadJson<T>(string path, int maximum, string? expectedHash = null)
    {
        NoLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 || file.Length > maximum) throw new IOException("Update record exceeds its size limit.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (expectedHash is not null && !Hash(bytes).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The update request changed after confirmation.");
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
            UpdateManifestCodec.RejectDuplicates(json.RootElement);
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new FormatException("Empty update record.");
        }
        catch (JsonException exception) { throw new FormatException("Malformed update record.", exception); }
    }

    internal static void WriteJson<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        NoLinks(path);
        WriteNew(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
        using var readback = OpenVerified(path, new(bytes.Length, Hash(bytes)));
    }

    internal static void WriteNew(string path, byte[] bytes)
    {
        NoLinks(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static UpdateFileVersion? Snapshot(string path)
    {
        NoLinks(path);
        if (Directory.Exists(path)) throw new IOException("A directory conflicts with an update file.");
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileSize) throw new IOException("An update file exceeds its size limit.");
        return new(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    internal static bool Matches(string path, UpdateFileVersion? version) => Snapshot(path) == version;
    internal static FileStream OpenVerified(string path, UpdateFileVersion version)
    {
        NoLinks(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != version.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A verified update file changed.");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static void CopyNew(Stream source, string path, UpdateFileVersion version, CancellationToken token = default)
    {
        NoLinks(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        NoLinks(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, version.Size - total + 1));
            if (count == 0) break;
            total += count;
            if (total > version.Size) throw new IOException("Update content exceeds its declared length.");
            hash.AppendData(buffer, 0, count);
            output.Write(buffer, 0, count);
        }
        if (total != version.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Update content size or hash mismatch.");
        output.Flush(true);
    }

    internal static string Root(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Update paths must be absolute local paths.");
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (full == System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetPathRoot(full)!))
            throw new IOException("A volume root cannot be an update directory.");
        NoLinks(full);
        return full;
    }

    internal static string Child(string root, string relative)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!IsInside(root, full)) throw new IOException("Update path escaped its directory.");
        NoLinks(full);
        return full;
    }

    internal static bool IsInside(string root, string path) => path.StartsWith(
        System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void NoLinks(string path)
    {
        for (var current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update paths cannot contain links or reparse points."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 220 || path.Contains('\\') || path.StartsWith('/') || path.EndsWith('/'))
            throw new IOException("Unsafe update archive path.");
        var parts = path.Split('/');
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 128 || !Regex.IsMatch(part, @"^[A-Za-z0-9][A-Za-z0-9_.()-]*$", RegexOptions.CultureInvariant) ||
                part.EndsWith('.') || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new IOException("Unsafe update archive path segment.");
            if (part.Equals("backups", StringComparison.OrdinalIgnoreCase) || part.Equals("updates", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("ctr_save.bin", StringComparison.OrdinalIgnoreCase) || part.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || part.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
                part.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Update archives cannot overwrite user settings, saves, backups or signing keys.");
        }
    }

    internal static IReadOnlyList<(ZipArchiveEntry Entry, string Path, UpdateFileVersion Version)> InspectZip(ZipArchive archive, CancellationToken token)
    {
        if (archive.Entries.Count is 0 or > MaximumFiles) throw new IOException("Update archive entry count exceeds its limit.");
        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(ZipArchiveEntry, string, UpdateFileVersion)>();
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var directory = entry.FullName.EndsWith('/');
            var path = directory ? entry.FullName[..^1] : entry.FullName;
            ValidateRelative(path);
            if (!paths.TryAdd(path, directory) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || entry.Length > MaximumFileSize)
                throw new IOException("Update archive has duplicates, links or oversized files.");
            if (directory) { if (entry.Length != 0) throw new IOException("Nonempty directory entry."); continue; }
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedSize) throw new IOException("Expanded update exceeds its limit.");
            using var stream = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, entry.Length - total + 1));
                if (read == 0) break;
                total += read;
                if (total > entry.Length) throw new IOException("Expanded file exceeds its declared size.");
                hash.AppendData(buffer, 0, read);
            }
            if (total != entry.Length) throw new IOException("Truncated update archive file.");
            files.Add((entry, path, new(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant())));
        }
        foreach (var path in paths.Keys)
        {
            for (var parent = path.LastIndexOf('/'); parent > 0; parent = path.LastIndexOf('/', parent - 1))
                if (paths.TryGetValue(path[..parent], out var directory) && !directory) throw new IOException("File/directory collision in update archive.");
        }
        foreach (var required in new[] { "CutTheSave.exe", "CutTheSave.dll", "CutTheSave.Updater.exe", "CutTheSave.deps.json", "CutTheSave.runtimeconfig.json" })
            if (!files.Any(file => file.Item2 == required)) throw new IOException("Update archive is missing a required application component.");
        return files.OrderBy(file => file.Item2, StringComparer.Ordinal).ToArray();
    }
}
