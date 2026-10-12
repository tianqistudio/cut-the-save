using System.IO;
using CutTheSave.Core;

namespace CutTheSave.App;

public partial class MainWindow
{
    private IDisposable? _openBackupLease;

    private async Task MaintainBackupRetentionAsync()
    {
        try
        {
            var result = await RunOperationAsync(() => _backupService.EnforceRetention(_openBackupPath is null ? [] : [_openBackupPath]));
            if (result.Issues.Count == 0) return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Backup housekeeping must never reclassify a successful save as failed.
        }
        var completedStatus = _statusTextFactory;
        SetStatus(() => completedStatus() + " " + LocalizationService.Get("BackupMaintenanceNotice"));
    }
}
