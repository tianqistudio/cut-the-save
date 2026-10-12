using System.Diagnostics;
using CutTheSave.Infrastructure.Windows;

internal static class RtssTransactionTests
{
    public static int RunChild(string[] args)
    {
        var (workflow, installation, _) = Create(args[1], args[2]);
        workflow.Apply60Fps(installation);
        return 72;
    }
    public static void Run()
    {
        var errors = new List<Exception>();
        foreach (var name in new[] { "changed-original", "changed-created", "changed-backup", "legacy", "crash-before-write", "crash-after-write", "undo-retry" })
        {
            try { RunCase(name); Console.WriteLine("PASS RTSS transaction " + name); }
            catch (Exception error) { errors.Add(error); Console.WriteLine("FAIL RTSS transaction " + name + ": " + error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    private static (RtssRepairWorkflow, RtssInstallation, RtssStateStore) Create(string root, string? crash = null)
    {
        var backend = new FakeRtssBackend(root);
        var store = new RtssStateStore(Path.Combine(root,"state.json"));
        var installation = new RtssInstallation(root,"test",true,true,false,null,Path.Combine(root,"RTSSHooks64.dll"));
        var workflow = new RtssRepairWorkflow(new RtssProfileService(_ => backend.CreateApi()),store,Path.Combine(root,"backups"))
        { Checkpoint = point => { if (point == crash) Environment.Exit(71); } };
        return (workflow, installation, store);
    }
    private static void RunCase(string name)
    {
        var root = Path.Combine(Path.GetTempPath(),"CutTheSave-rtss-journal-" + Guid.NewGuid().ToString("N"));
        Console.WriteLine("FIXTURE " + root);
        Directory.CreateDirectory(Path.Combine(root,"Profiles"));
        File.WriteAllText(Path.Combine(root,"RTSSHooks64.dll"),"fake");
        var profile = Path.Combine(root,"Profiles",RtssProfileService.TargetProfile + ".cfg");
        const string original = "FramerateLimit=30\nFramerateLimitDenominator=1\n";
        if (name != "changed-created") File.WriteAllText(profile,original);
        var (workflow, installation, store) = Create(root);
        if (name == "legacy")
        {
            store.Save(new(root,RtssProfileService.TargetProfile,true,null,DateTimeOffset.UtcNow));
            Reject(() => workflow.Undo(installation)); Check(File.ReadAllText(profile)==original && store.StateFileExists); return;
        }
        if (name.StartsWith("crash-"))
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(RtssTransactionTests).Assembly.Location);
            foreach (var arg in new[]{"--rtss-transaction-crash",root,name=="crash-before-write" ? "after-intent" : "after-profile"}) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            Check(child.WaitForExit(20000) && child.ExitCode==71);
            Check(store.Load()?.Phase=="prepared");
            if (name=="crash-before-write") { workflow.Undo(installation); Check(File.ReadAllText(profile)==original && !store.StateFileExists); }
            else { Reject(() => workflow.Undo(installation)); Check(store.StateFileExists && File.ReadAllText(profile).Contains("FramerateLimit=60")); }
            return;
        }
        workflow.Apply60Fps(installation);
        if (name=="changed-backup") File.AppendAllText(store.Load()!.BackupPath!,"changed");
        else if (name=="undo-retry")
        {
            // Simulate an exit after restoring bytes, but before clearing the durable undo intent.
            store.Save(store.Load()! with { Phase="undoing" });
            File.WriteAllText(profile,original);
            workflow.Undo(installation); Check(!store.StateFileExists && File.ReadAllText(profile)==original); return;
        }
        else File.AppendAllText(profile,"OSD=external-user-change\n");
        var before = File.ReadAllText(profile);
        Reject(() => workflow.Undo(installation));
        Check(File.ReadAllText(profile)==before && store.StateFileExists);
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException) { return; }
        throw new Exception("Unsafe RTSS recovery was accepted.");
    }
    private static void Check(bool value) { if (!value) throw new Exception("RTSS journal invariant failed."); }
}
