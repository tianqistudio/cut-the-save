using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using CutTheSave.Infrastructure.Windows;

namespace CutTheSave.App;

internal enum RtssHelperOperation
{
    Apply,
    Undo
}

internal enum RtssHelperExitCode
{
    Success = 0,
    AppliedButRtssNotRunning = 10,
    InvalidArguments = 20,
    ElevationRequired = 21,
    GameRunning = 22,
    InstallationNotFound = 23,
    ActiveRepairExists = 24,
    RepairStateMissing = 25,
    RepairStateInvalid = 26,
    AccessDenied = 27,
    RtssUnavailable = 28,
    OperationFailed = 29,
    UserCanceled = 30,
    OperationBusy = 31,
    UnverifiedRtss = 32
}

internal readonly record struct RtssHelperInvocation(
    bool IsHelperMode,
    bool IsValid,
    RtssHelperOperation Operation,
    string? InstallPath);

internal static class RtssHelperProtocol
{
    internal const string CommandSwitch = "--cut-the-save-rtss-helper";

    public static RtssHelperInvocation Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || !string.Equals(args[0], CommandSwitch, StringComparison.Ordinal))
            return new RtssHelperInvocation(false, false, default, null);

        if (args.Count != 3 || string.IsNullOrWhiteSpace(args[2]))
            return new RtssHelperInvocation(true, false, default, null);

        var operation = args[1] switch
        {
            "apply" => RtssHelperOperation.Apply,
            "undo" => RtssHelperOperation.Undo,
            _ => (RtssHelperOperation?)null
        };

        return operation is null
            ? new RtssHelperInvocation(true, false, default, null)
            : new RtssHelperInvocation(true, true, operation.Value, args[2]);
    }

    public static IReadOnlyList<string> BuildArguments(RtssHelperOperation operation, string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException("RTSS install path is required.", nameof(installPath));

        return
        [
            CommandSwitch,
            operation == RtssHelperOperation.Apply ? "apply" : "undo",
            installPath
        ];
    }
}

internal static class RtssElevatedCommandRunner
{
    public static RtssHelperExitCode Run(RtssHelperInvocation invocation)
    {
        if (!invocation.IsHelperMode || !invocation.IsValid || string.IsNullOrWhiteSpace(invocation.InstallPath))
            return RtssHelperExitCode.InvalidArguments;
        if (!IsElevated())
            return RtssHelperExitCode.ElevationRequired;
        if (IsProcessRunning(Path.GetFileNameWithoutExtension(RtssProfileService.TargetProfile)))
            return RtssHelperExitCode.GameRunning;

        RtssInstallation? installation;
        try
        {
            installation = new RtssDetector()
                .FindInstallations()
                .Where(item => item.HasExecutable && item.HasHooks)
                .SingleOrDefault(item => PathsEqual(item.InstallPath, invocation.InstallPath));
        }
        catch (InvalidOperationException)
        {
            return RtssHelperExitCode.InstallationNotFound;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return RtssHelperExitCode.RtssUnavailable;
        }

        if (installation is null)
            return RtssHelperExitCode.InstallationNotFound;
        if (!installation.Compatibility.CanUseProfileApi)
            return RtssHelperExitCode.UnverifiedRtss;

        try
        {
            using var mutex = new Mutex(false, @"Global\CutTheSave.RtssRepair");
            var lockTaken = false;
            try
            {
                try
                {
                    lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(15));
                }
                catch (AbandonedMutexException)
                {
                    lockTaken = true;
                }

                return lockTaken
                    ? RunLocked(invocation.Operation, installation)
                    : RtssHelperExitCode.OperationBusy;
            }
            finally
            {
                if (lockTaken)
                    mutex.ReleaseMutex();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return RtssHelperExitCode.AccessDenied;
        }
    }

    private static RtssHelperExitCode RunLocked(RtssHelperOperation operation, RtssInstallation installation)
    {
        var stateStore = new RtssStateStore();
        if (operation == RtssHelperOperation.Apply && stateStore.StateFileExists)
            return RtssHelperExitCode.ActiveRepairExists;
        if (operation == RtssHelperOperation.Undo && !stateStore.StateFileExists)
            return RtssHelperExitCode.RepairStateMissing;

        var persisted = stateStore.Load();
        if (operation == RtssHelperOperation.Undo)
        {
            if (persisted is null)
                return RtssHelperExitCode.RepairStateInvalid;
            if (!PathsEqual(persisted.InstallPath, installation.InstallPath) ||
                !string.Equals(persisted.ProfileName, RtssProfileService.TargetProfile, StringComparison.OrdinalIgnoreCase))
            {
                return RtssHelperExitCode.RepairStateInvalid;
            }
        }

        try
        {
            var workflow = new RtssRepairWorkflow();
            if (operation == RtssHelperOperation.Apply)
            {
                var applied = workflow.Apply60Fps(installation);
                if (applied.FramerateLimit != RtssProfileService.TargetFps ||
                    applied.FramerateLimitDenominator != 1 ||
                    string.IsNullOrWhiteSpace(applied.ProfilePath))
                {
                    return RtssHelperExitCode.OperationFailed;
                }

                return EnsureRtssRunning(installation)
                    ? RtssHelperExitCode.Success
                    : RtssHelperExitCode.AppliedButRtssNotRunning;
            }

            workflow.Undo(installation);
            return RtssHelperExitCode.Success;
        }
        catch (UnauthorizedAccessException)
        {
            return RtssHelperExitCode.AccessDenied;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return RtssHelperExitCode.RtssUnavailable;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or AggregateException)
        {
            return RtssHelperExitCode.OperationFailed;
        }
    }

    private static bool EnsureRtssRunning(RtssInstallation installation)
    {
        if (IsProcessRunning("RTSS"))
            return true;
        if (!installation.HasExecutable || string.IsNullOrWhiteSpace(installation.ExecutablePath) || !File.Exists(installation.ExecutablePath))
            return false;

        try
        {
            using var binaryLease = RtssCompatibility.LockVerifiedBinaries(installation.InstallPath);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                WorkingDirectory = installation.InstallPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
                if (IsProcessRunning("RTSS"))
                {
                    Thread.Sleep(500);
                    return IsProcessRunning("RTSS");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Any(process => !process.HasExited);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

internal sealed class RtssElevationBroker
{
    public async Task<RtssHelperExitCode> RunAsync(RtssHelperOperation operation, RtssInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            throw new InvalidOperationException("Cannot locate the running CutTheSave executable.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in RtssHelperProtocol.BuildArguments(operation, installation.InstallPath))
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the RTSS permission helper.");
            await process.WaitForExitAsync();
            return Enum.IsDefined(typeof(RtssHelperExitCode), process.ExitCode)
                ? (RtssHelperExitCode)process.ExitCode
                : RtssHelperExitCode.OperationFailed;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return RtssHelperExitCode.UserCanceled;
        }
    }
}
