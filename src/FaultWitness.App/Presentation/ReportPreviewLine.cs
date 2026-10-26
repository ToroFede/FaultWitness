namespace FaultWitness.App.Presentation;

/// <summary>Display-only formatting of the existing export. SourceText is never rewritten or exported.</summary>
public sealed record ReportPreviewLine(string SourceText, string Text, int HeadingLevel, bool IsBullet)
{
    public bool IsTitle => HeadingLevel == 1;
    public bool IsHeading => HeadingLevel > 1;
    public bool IsBlank => SourceText.Length == 0;

    public static IReadOnlyList<ReportPreviewLine> Format(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n').Select(line =>
        {
            var level = line.StartsWith("### ", StringComparison.Ordinal) ? 3 :
                line.StartsWith("## ", StringComparison.Ordinal) ? 2 : line.StartsWith("# ", StringComparison.Ordinal) ? 1 : 0;
            var bullet = line.StartsWith("- ", StringComparison.Ordinal);
            var text = level > 0 ? line[(level + 1)..] : bullet ? "• " + line[2..] : line;
            return new ReportPreviewLine(line, text, level, bullet);
        }).ToArray();
}
