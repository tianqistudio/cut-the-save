using System.ComponentModel;
using CutTheSave.Infrastructure.Windows;

namespace CutTheSave.App;

internal static class SaveWriteGuard
{
    internal static void EnsureGameClosed(Func<bool>? isRunning = null)
    {
        bool running;
        try
        {
            running = (isRunning ?? GameProcessGuard.IsRunning)();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(LocalizationService.Get("GameStateUnknown"), exception);
        }
        if (running)
            throw new InvalidOperationException(LocalizationService.Get("CloseGameBeforeSave"));
    }
}
