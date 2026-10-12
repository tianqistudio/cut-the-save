using System.IO;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class MainWindow
{
    internal SaveTargetState? ReviewSaveDestination(string path, Func<string, bool> confirmOverwrite)
    {
        var fullPath = Path.GetFullPath(path);
        var expected = _loadedPath is not null && SaveTargetState.SamePath(fullPath, _loadedPath)
            ? _diskVersion ?? SaveTargetState.Capture(fullPath) : SaveTargetState.Capture(fullPath);
        expected.EnsureCurrent(fullPath);
        return expected.Exists && !confirmOverwrite(fullPath) ? null : expected;
    }

    internal static string DescribeSaveFailure(Exception exception) => exception switch
    {
        BackupMaintenancePendingException => LocalizationService.Get("BackupRepairPending"),
        SaveRecoveryPendingException => LocalizationService.Get("SaveRecoveryPending"),
        SaveTargetChangedException => LocalizationService.Get("SaveTargetChanged"),
        SaveWriteFailureException failure => LocalizationService.Get(failure.Outcome switch
        {
            SaveRecoveryOutcome.OriginalRestored => "WriteFailedRestored",
            SaveRecoveryOutcome.CurrentFileRetained => "WriteFailedRetained",
            _ => "WriteRecoveryReview"
        }) + Environment.NewLine + string.Join(Environment.NewLine, failure.RecoveryPaths),
        _ => exception.Message
    };
}
