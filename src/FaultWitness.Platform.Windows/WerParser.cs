using System.Globalization;
using System.Text;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

public static class WerParser
{
    public static NormalizedEvent Parse(Stream input, DateTimeOffset fallbackTimestamp, string provenance,
        ImportLimits limits, CancellationToken cancellationToken)
    {
        if (input.Length > limits.WerBytes) throw new InvalidDataException("WER size limit exceeded.");
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), true, 4096, true);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > limits.WerLines || line.Length > limits.LineCharacters) throw new InvalidDataException("WER line limit exceeded.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            var index = line.IndexOf('=');
            if (index <= 0 || !fields.TryAdd(line[..index], line[(index + 1)..])) throw new InvalidDataException("Malformed or duplicate WER key.");
        }
        if (!fields.TryGetValue("EventType", out var type) || string.IsNullOrWhiteSpace(type))
            throw new InvalidDataException("Missing WER event type.");
        fields["Format"] = "WER";
        var process = fields.GetValueOrDefault("AppName");
        var module = fields.GetValueOrDefault("FaultModuleName");
        if (type.Equals("APPCRASH", StringComparison.OrdinalIgnoreCase))
        {
            process ??= fields.GetValueOrDefault("Sig[0].Value");
            module ??= fields.GetValueOrDefault("Sig[3].Value");
            Copy(fields, "Sig[1].Value", "AppVersion");
            Copy(fields, "Sig[4].Value", "ModuleVersion");
            Copy(fields, "Sig[6].Value", "ExceptionCode");
        }
        if (!type.Equals("LiveKernelEvent", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(process))
            throw new InvalidDataException("Missing WER application identity.");
        Copy(fields, "ReportIdentifier", "ReportId");
        var timestamp = fallbackTimestamp.ToUniversalTime();
        fields["TimestampBasis"] = "FileWriteTime";
        if (fields.TryGetValue("EventTime", out var value))
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileTime))
                throw new InvalidDataException("Invalid WER timestamp.");
            try { timestamp = DateTimeOffset.FromFileTime(fileTime); }
            catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("Invalid WER timestamp.", exception); }
            fields["TimestampBasis"] = "EventTime";
        }
        return new NormalizedEvent(Guid.NewGuid(), SourceType.Imported, "Windows", timestamp, null,
            "Windows Error Reporting", 1001, null, IncidentSeverity.Medium, process, null, module, null, fields, provenance);
    }

    private static void Copy(Dictionary<string, string> fields, string source, string target)
    {
        if (!fields.ContainsKey(target) && fields.TryGetValue(source, out var value)) fields[target] = value;
    }
}
