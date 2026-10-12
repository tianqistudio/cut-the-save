using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CutTheSave.Core;
using NSec.Cryptography;

internal static class UpdateTransactionTests
{
    public static int RunChild(string[] args)
    {
        var transaction = new UpdatePackageTransaction(new(Convert.FromBase64String(args[3])), "test-key", "stable")
            { Checkpoint = point => { if (point == args[4]) Environment.Exit(71); } };
        transaction.Install(args[1], args[2]);
        return 72;
    }

    public static void Run()
    {
        var cases = new[] { "install-undo", "runtime-case", "request-hash-case", "launch-failure", "failure-rollback", "changed-target", "changed-payload", "changed-backup", "external-after-install", "crash", "version-mismatch", "bad-zip", "cancelled" };
        var errors = new List<Exception>();
        foreach (var name in cases)
        {
            try { RunCase(name); Console.WriteLine("PASS update transaction " + name); }
            catch (Exception error) { Console.WriteLine("FAIL update transaction " + name + ": " + error); errors.Add(error); }
        }
        if (!UpdateWorkerProtocol.IsValid(["--install", Path.GetFullPath("request.json"), "--request-sha256", new('a',64)])
            || UpdateWorkerProtocol.IsValid(["--source","a","--target","b","--pid","99999"]))
            errors.Add(new Exception("Updater command protocol mismatch."));
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    private static void RunCase(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "CutTheSave-update-contract-" + Guid.NewGuid().ToString("N"));
        Console.WriteLine("FIXTURE " + root);
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "CutTheSave.exe"), "original program (not executable)");
        var worker = Path.Combine(root, "worker.exe");
        File.WriteAllText(worker, "synthetic worker; never launched");
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var transaction = new UpdatePackageTransaction(new(publicKey), "test-key", "stable")
        { Checkpoint = point => { if (name == "failure-rollback" && point == "after-file-1") throw new IOException("injected write failure"); } };
        using var packageStream = new MemoryStream();
        using (var zip = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in new[] {"CutTheSave.exe", "CutTheSave.dll", "CutTheSave.Updater.exe", "CutTheSave.deps.json", "CutTheSave.runtimeconfig.json"})
            {
                using var output = zip.CreateEntry(file).Open();
                var content = file == "CutTheSave.dll" ? File.ReadAllBytes(typeof(UpdateTransactionTests).Assembly.Location) : Encoding.UTF8.GetBytes("synthetic new " + file);
                output.Write(content);
            }
        }
        var package = name == "bad-zip" ? "not a zip"u8.ToArray() : packageStream.ToArray();
        var version = name == "version-mismatch" ? "999.0.0" : typeof(UpdateTransactionTests).Assembly.GetName().Version!.ToString(3);
        var manifest = UpdateSecurityTests.Manifest(version) with { Assets = [new(name == "runtime-case" ? "WIN-X64" : "win-x64","CutTheSave.zip",package.Length,Convert.ToHexString(SHA256.HashData(package)),[new("a","https://a.example/update.zip")])] };
        var bytes = UpdateManifestCodec.Serialize(manifest);
        var signed = new VerifiedManifestSource("test",manifest,bytes) { SignatureBytes = SignatureAlgorithm.Ed25519.Sign(key,bytes) };
        if (name == "runtime-case")
            Check(new UpdateDecisionService().Choose("0.0.0", [signed]).Kind == UpdateDecisionKind.Available);
        using var parent = Process.GetCurrentProcess();
        PreparedUpdate Prepare() => transaction.Prepare(signed,package,target,Path.Combine(root,"cache"),worker,parent.Id,parent.StartTime.ToUniversalTime().Ticks,
            name == "cancelled" ? new CancellationToken(true) : CancellationToken.None);
        if (name is "bad-zip" or "version-mismatch" or "cancelled") { Reject(() => Prepare()); return; }
        var prepared = Prepare();
        var request = transaction.ReadRequest(prepared.RequestPath,prepared.RequestSha256);
        var job = Path.GetDirectoryName(prepared.RequestPath)!;
        if (name == "request-hash-case")
        {
            var uppercaseHash = prepared.RequestSha256.ToUpperInvariant();
            Check(UpdateWorkerProtocol.IsValid(["--install", prepared.RequestPath, "--request-sha256", uppercaseHash]));
            Check(transaction.ReadState(prepared.RequestPath, uppercaseHash).Status == "prepared");
            Check(transaction.Install(prepared.RequestPath, uppercaseHash).Status == "committed");
            // The next invocation may use the original lower-case hash even when
            // the previous worker wrote its recovery record using upper case.
            Check(transaction.ReadState(prepared.RequestPath, prepared.RequestSha256).Status == "committed");
            Check(transaction.Rollback(prepared.RequestPath, prepared.RequestSha256).Status == "rolled-back");
            Check(File.ReadAllText(Path.Combine(target, "CutTheSave.exe")) == "original program (not executable)");
            Check(!File.Exists(Path.Combine(target, "CutTheSave.dll")));
            return;
        }
        if (name == "changed-target") { File.AppendAllText(Path.Combine(target,"CutTheSave.exe"),"external"); Reject(() => transaction.Install(prepared.RequestPath,prepared.RequestSha256)); return; }
        if (name == "changed-payload") { File.AppendAllText(Path.Combine(job,"payload","CutTheSave.exe"),"external"); Reject(() => transaction.Install(prepared.RequestPath,prepared.RequestSha256)); return; }
        if (name == "launch-failure")
        {
            var result = UpdateWorkerExecution.Execute(transaction,prepared.RequestPath,prepared.RequestSha256,false,
                () => throw new System.ComponentModel.Win32Exception(5));
            Check(result.ExitCode == 7 && result.Status == "launch-failed");
            Check(transaction.ReadState(prepared.RequestPath,prepared.RequestSha256).Status == "committed");
            Check(File.Exists(Path.Combine(job,"worker-result.json"))); return;
        }
        if (name == "crash")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(UpdateTransactionTests).Assembly.Location);
            foreach (var arg in new[]{"--update-transaction-crash",prepared.RequestPath,prepared.RequestSha256,Convert.ToBase64String(publicKey),"after-file-1"}) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(20000) || child.ExitCode != 71) throw new Exception("The injected process exit was not observed.");
            Check(transaction.ReadState(prepared.RequestPath,prepared.RequestSha256).Status == "applying");
        }
        else
        {
            var installed = transaction.Install(prepared.RequestPath,prepared.RequestSha256);
            Check(installed.Status == (name == "failure-rollback" ? "rolled-back" : "committed"));
        }
        if (name == "external-after-install")
        {
            File.AppendAllText(Path.Combine(target,"CutTheSave.exe"),"external");
            Check(transaction.Rollback(prepared.RequestPath,prepared.RequestSha256).Status == "needs-review");
            Check(File.ReadAllText(Path.Combine(target,"CutTheSave.exe")).EndsWith("external")); return;
        }
        if (name == "changed-backup")
        {
            var index = request.Files.ToList().FindIndex(f => f.Path == "CutTheSave.exe");
            File.AppendAllText(Path.Combine(job,"backups",index+".bin"),"external");
            Check(transaction.Rollback(prepared.RequestPath,prepared.RequestSha256).Status == "needs-review"); return;
        }
        Check(transaction.Rollback(prepared.RequestPath,prepared.RequestSha256).Status == "rolled-back");
        Check(File.ReadAllText(Path.Combine(target,"CutTheSave.exe")) == "original program (not executable)");
        Check(!File.Exists(Path.Combine(target,"CutTheSave.dll")));
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException) { return; }
        throw new Exception("Unsafe transaction was accepted.");
    }
    private static void Check(bool value) { if (!value) throw new Exception("Transaction invariant failed."); }
}
