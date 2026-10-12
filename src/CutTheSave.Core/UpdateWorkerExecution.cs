using System.ComponentModel;

namespace CutTheSave.Core;

public sealed record UpdateWorkerResult(int SchemaVersion, string RequestSha256, string Status, int ExitCode, string? Detail);

public static class UpdateWorkerExecution
{
    // Process launch is supplied by the trusted host, allowing failure tests without
    // executing an installer or starting an application with real user preferences.
    public static UpdateWorkerResult Execute(UpdatePackageTransaction transaction, string requestPath, string hash, bool rollback, Action launch)
    {
        var state = rollback ? transaction.Rollback(requestPath, hash) : transaction.Install(requestPath, hash);
        var result = new UpdateWorkerResult(1, hash, state.Status, state.Status == "committed" ? 0 : state.Status == "rolled-back" ? 8 : 6, state.Detail);
        if (state.Status is "committed" or "rolled-back")
        {
            try { launch(); }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
            {
                // A launch failure cannot undo an already verified file transaction.
                result = result with { Status = "launch-failed", ExitCode = 7, Detail = exception.Message };
            }
        }
        UpdatePackageFiles.WriteJson(UpdatePackageFiles.Child(UpdatePackageFiles.Root(Path.GetDirectoryName(Path.GetFullPath(requestPath))!), "worker-result.json"), result);
        return result;
    }
}
