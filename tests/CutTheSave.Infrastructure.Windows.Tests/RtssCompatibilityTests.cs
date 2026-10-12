using CutTheSave.Infrastructure.Windows;

internal static class RtssCompatibilityTests
{
    internal static void Run()
    {
        const string exe = "84e6e439d313dcee0ba9549d8248d3923d9de6805d0380be5eab337446856736";
        const string hooks = "68c496dea7ded5b8766092f0159f4e9ba3947df0829f594eb6981d2b06352ad5";
        Check(RtssCompatibility.AssessEvidence("7.3.5.28314", exe, hooks).Kind == RtssCompatibilityKind.ProfileApiVerified, "exact pair only API-verified, not Known Good");
        foreach (var version in new[] { null, "", "7.3.5", "7.3.5.28315", "7.3.7", "999.0.0.0", "7.3.5.28314 beta" })
            Check(!RtssCompatibility.AssessEvidence(version, exe, hooks).CanUseProfileApi, "unknown version denied");
        Check(!RtssCompatibility.AssessEvidence("7.3.5.28314", exe, new string('f', 64)).CanUseProfileApi, "hooks mismatch denied");
        Check(!RtssCompatibility.AssessEvidence("7.3.5.28314", null, hooks).CanUseProfileApi, "missing executable hash denied");
        var root = Directory.CreateTempSubdirectory("CutTheSave-rtss-compat-");
        try
        {
            var hooksPath = Path.Combine(root.FullName, "RTSSHooks64.dll");
            File.WriteAllText(hooksPath, "not a native DLL");
            File.WriteAllText(Path.Combine(root.FullName, "RTSS.exe"), "not an executable");
            Check(!RtssCompatibility.Inspect(root.FullName).CanUseProfileApi, "synthetic binaries never loaded or approved");
            var installation = new RtssInstallation(root.FullName, "7.3.5.28314", true, true, false, null, hooksPath)
                { Compatibility = new(RtssCompatibilityKind.KnownGood, "7.3.5.28314") };
            var service = new RtssProfileService();
            var backups = Path.Combine(root.FullName, "must-not-create");
            Denied(() => service.Read(installation));
            Denied(() => service.Apply60Fps(installation, backups));
            Denied(() => service.Restore(installation, hooksPath));
            Denied(() => service.RemoveCreatedProfile(installation));
            Denied(() => service.Read(installation, "Global"));
            Denied(() => service.Apply60Fps(installation, backups, "../Global"));
            Check(!Directory.Exists(backups) && !Directory.Exists(Path.Combine(root.FullName, "Profiles")), "rejection precedes backup/profile writes");
        }
        finally { if (Environment.GetEnvironmentVariable("CTS_RETAIN_TEST_FILES") != "1") root.Delete(recursive: true); }
        Console.WriteLine("PASS RTSS exact-version/hash policy; forged flags and Global/path Profile requests rejected before writes");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    private static void Denied(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unverified RTSS operation accepted"); }
}
