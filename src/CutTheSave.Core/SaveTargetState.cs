using System.Security.Cryptography;
using System.Text;

namespace CutTheSave.Core;

public sealed class SaveTargetChangedException() : IOException("The destination changed since it was reviewed. Reopen it or save to another file.");

public sealed record SaveTargetState(string Path, bool Exists, long Length, string? Sha256)
{
    public static SaveTargetState Capture(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > SaveCodec.MaximumFileBytes)
                throw new IOException("The destination exceeds the save size limit; it will not be overwritten.");
            return new(fullPath, true, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
        catch (FileNotFoundException) { return new(fullPath, false, 0, null); }
        catch (DirectoryNotFoundException) { return new(fullPath, false, 0, null); }
    }

    public static SaveTargetState FromBytes(string path, byte[] bytes) =>
        new(System.IO.Path.GetFullPath(path), true, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    public void EnsureCurrent(string path)
    {
        var actual = Capture(path);
        if (!SamePath(Path, actual.Path) || Exists != actual.Exists
            || Length != actual.Length || !string.Equals(Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new SaveTargetChangedException();
    }

    public static bool SamePath(string first, string second) =>
        string.Equals(System.IO.Path.GetFullPath(first), System.IO.Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static IDisposable BeginWrite(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
        var name = "CutTheSave.Save." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        var mutex = new Mutex(false, name);
        try
        {
            try { if (!mutex.WaitOne(0)) throw new IOException("Another save operation is using this destination."); }
            catch (AbandonedMutexException) { /* Revalidate target state before writing. */ }
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
