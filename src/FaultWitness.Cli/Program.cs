using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;

namespace FaultWitness.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") return Usage();
        if (args[0] is "--version") { Console.WriteLine("FaultWitness 0.9.0-private"); return 0; }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        try
        {
            var now = DateTimeOffset.UtcNow;
            EventBatch batch;
            DateTimeOffset started;
            var examinedTo = now;
            switch (args[0].ToLowerInvariant())
            {
                case "scan":
                    started = now - ParseDuration(OptionValue(args, "--last") ?? "7d");
                    batch = await new WindowsDiagnosticsProvider().ReadAsync(started, now, cancellation.Token).ConfigureAwait(false); break;
                case "around" when args.Length > 1 && DateTimeOffset.TryParse(args[1], out var time):
                    started = time.ToUniversalTime().AddMinutes(-5);
                    examinedTo = time.ToUniversalTime().AddMinutes(5);
                    batch = await new WindowsDiagnosticsProvider().ReadAsync(started, examinedTo, cancellation.Token).ConfigureAwait(false); break;
                case "import" when args.Length > 1:
                    started = now;
                    var imported = await WindowsImportService.ImportAsync(args.Skip(1), cancellation.Token).ConfigureAwait(false);
                    batch = imported.Batch;
                    foreach (var error in imported.Errors) Console.Error.WriteLine(error);
                    if (imported.Errors.Count > 0 && batch.Events.Count == 0) return 1;
                    break;
                case "export":
                    started = now.AddDays(-7);
                    batch = await new WindowsDiagnosticsProvider().ReadAsync(started, now, cancellation.Token).ConfigureAwait(false); break;
                default: return Usage();
            }
            var result = new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, started, examinedTo, cancellation.Token);
            var json = string.Equals(OptionValue(args, "--format"), "json", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(json ? ReportExporter.ToJson(result, new ExportPrivacyOptions()) : ReportExporter.ToMarkdown(result, new ExportPrivacyOptions()));
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Operation cancelled."); return 130; }
        catch (Exception exception) { Console.Error.WriteLine($"FaultWitness could not complete the request: {exception.GetType().Name}."); return 1; }
    }

    private static string? OptionValue(string[] args, string option) { var index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static TimeSpan ParseDuration(string value) => value.EndsWith("d", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[..^1], out var days) && days is > 0 and <= 90 ? TimeSpan.FromDays(days) : TimeSpan.FromDays(7);
    private static int Usage() { Console.Error.WriteLine("faultwitness scan --last 7d | around \"2026-08-28 22:37\" | import <file> | export --format json"); return 2; }
}
