namespace CutTheSave.Core;

public static class UpdateWorkerProtocol
{
    public static bool IsValid(string[] args) => args.Length == 4
        && args[0] is "--install" or "--rollback" && args[2] == "--request-sha256"
        && Path.IsPathFullyQualified(args[1]) && args[3].Length == 64 && args[3].All(Uri.IsHexDigit);
}
