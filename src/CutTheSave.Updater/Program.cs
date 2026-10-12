using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using CutTheSave.Core;

if (!UpdateWorkerProtocol.IsValid(args))
{
    Console.Error.WriteLine("Usage: CutTheSave.Updater (--install | --rollback) <request.json> --request-sha256 <sha256>");
    return 2;
}

try
{
    // No network, key argument, environment-key override or administrator request.
    // The published standalone executable embeds the same trust as the main app.
    var transaction = ReleaseUpdateTrust.CreateTransaction();
    var requestPath = Path.GetFullPath(args[1]);
    var cache = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CutTheSave", "updates"))
        + Path.DirectorySeparatorChar;
    if (!Path.IsPathFullyQualified(args[1]) || !requestPath.StartsWith(cache, StringComparison.OrdinalIgnoreCase))
        throw new IOException("The request must be in this user's CutTheSave update cache.");
    var request = transaction.ReadRequest(requestPath, args[3]);
    WaitForParent(request);
    var result = UpdateWorkerExecution.Execute(transaction, requestPath, args[3], args[0] == "--rollback", () =>
    {
        var launch = new ProcessStartInfo(Path.Combine(request.TargetDirectory, "CutTheSave.exe"))
            { UseShellExecute = false, WorkingDirectory = request.TargetDirectory };
        using var process = Process.Start(launch) ?? throw new InvalidOperationException("The application did not start.");
    });
    Console.WriteLine($"Update result: {result.Status}; evidence: {Path.GetDirectoryName(requestPath)}");
    return result.ExitCode;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
    ArgumentException or FormatException or Win32Exception or CryptographicException)
{
    Console.Error.WriteLine($"Update stopped without claiming success: {exception.Message}");
    return 5;
}

static void WaitForParent(UpdateInstallRequest request)
{
    Process process;
    try { process = Process.GetProcessById(request.ParentProcessId); }
    catch (ArgumentException) { return; } // It exited before this worker started.
    using (process)
    {
        if (process.HasExited) return;
        if (process.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks ||
            !string.Equals(process.MainModule?.FileName, Path.Combine(request.TargetDirectory, "CutTheSave.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The parent process identity changed; no process will be terminated.");
        if (!process.WaitForExit(30_000))
            throw new IOException("The main application is still running; update was not applied.");
    }
}
