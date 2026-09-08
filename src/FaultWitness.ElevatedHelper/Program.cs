using Microsoft.Win32;

namespace FaultWitness.ElevatedHelper;

internal static class Program
{
    private const string LocalDumpsRoot = "SOFTWARE\\Microsoft\\Windows\\Windows Error Reporting\\LocalDumps";
    private static int Main(string[] args)
    {
        if (args.Length < 2 || (args[0] is not "enable-local-dumps" and not "restore-local-dumps") || !IsSafeExecutableName(args[1])) return 2;
        var keyPath = $"{LocalDumpsRoot}\\{args[1]}";
        if (args[0] == "restore-local-dumps") { Registry.LocalMachine.DeleteSubKeyTree(keyPath, false); return 0; }
        var dumpType = args.Contains("--full", StringComparer.OrdinalIgnoreCase) ? 2 : 1;
        var count = ParseCount(args);
        using var key = Registry.LocalMachine.CreateSubKey(keyPath, true);
        if (key is null) return 1;
        key.SetValue("DumpType", dumpType, RegistryValueKind.DWord); key.SetValue("DumpCount", count, RegistryValueKind.DWord);
        return 0;
    }
    private static bool IsSafeExecutableName(string value) => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(value) == value && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    private static int ParseCount(string[] args) => int.TryParse(args.SkipWhile(argument => argument != "--count").Skip(1).FirstOrDefault(), out var count) && count is >= 1 and <= 10 ? count : 3;
}
