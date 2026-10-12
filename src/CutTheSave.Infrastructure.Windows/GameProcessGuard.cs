using System.Diagnostics;

namespace CutTheSave.Infrastructure.Windows;

public static class GameProcessGuard
{
    // Steam's launcher and the actual renderer can have independent lifetimes.
    public static bool IsRunning()
    {
        foreach (var name in new[] { "CutTheRopeApp", "CutTheRope" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                            return true;
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between enumeration and inspection.
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        return false;
    }
}
