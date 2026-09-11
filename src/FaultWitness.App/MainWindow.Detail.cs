using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using FaultWitness.Core;
using FaultWitness.Rules;
using FaultWitness.Export;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private Control BuildDetail()
    {
        if (ViewModel.Selected is not { } row) return Empty("SelectIncident");
        var incident = row.Incident;
        var body = Stack(Actions(SubtleButton("Back", () => ViewModel.Navigate(AppPage.Incidents)), Button("CopyForSupport", () => { exportSelectedOnly = true; ViewModel.Navigate(AppPage.Export); }, "DetailCopy")),
            Label(row.Title, TextRole.PageTitle), Muted(row.Timestamp + " · " + row.Strength + " · " + ViewModel.Origin),
            Section("WhatHappened", Label(row.Assessment, TextRole.SectionTitle)));
        body.Children.Add(Muted(T(row.SharedReportCount > 1 ? "SharedReportHelp" : "SeparateOccurrence")));
        if (row.DevelopmentContext.Length > 0) body.Children.Add(Label(row.DevelopmentContext));
        if (row.SharedReportCount > 1) body.Children.Add(Label(row.SharedReport));
        var evidence = new StackPanel { Spacing = D("primitive.space.3"), Name = "EvidencePanel" };
        foreach (var kind in Enum.GetValues<EvidenceKind>())
        {
            var items = incident.Evidence.Where(item => item.Kind == kind).DistinctBy(item => (item.Family, item.LocalizationKey, item.SourceEvent?.Id)).ToArray();
            var symbol = kind == EvidenceKind.Positive ? "+" : kind == EvidenceKind.Negative ? "−" : "?";
            var group = Stack(Label(symbol + "  " + T("Evidence" + kind), TextRole.RowTitle));
            if (items.Length == 0) group.Children.Add(Muted(T("NoEvidenceInGroup")));
            foreach (var item in items.Take(4)) group.Children.Add(Label(EvidenceText(item)));
            if (items.Length > 4)
            {
                var full = new ListBox { ItemsSource = items.Select(EvidenceText).ToArray(), Height = 180 };
                group.Children.Add(Expand("MoreEvidence", full));
            }
            evidence.Children.Add(group);
        }
        var findings = incident.Findings.Where(item => item.Disposition != FindingDisposition.Suppressed).ToArray();
        var actions = findings.SelectMany(item => item.RecommendedActionKeys).Distinct().ToArray();
        body.Children.Add(Surface(Stack(Label(T("BestNextStep"), TextRole.SectionTitle), Label(actions.Length == 0 ? T("NoActionAvailable") : T(actions[0]), TextRole.RowTitle), Expand("WhyThisTest", Label(T("WhyEvidenceFirst"))))));
        body.Children.Add(Section("Evidence", evidence));
        body.Children.Add(Expand("Timeline", Timeline(incident)));
        body.Children.Add(Section("Assessment", Stack(findings.Select(item => Label(T(item.InterpretationKey))).DistinctBy(item => item.Text).Cast<Control>().ToArray())));
        var hypotheses = findings.SelectMany(item => item.HypothesisKeys).Distinct().ToArray();
        body.Children.Add(Expand("PossibleExplanations", hypotheses.Length == 0 ? Label(T("NoCauseEstablished")) : Stack(hypotheses.Select(key => (Control)Label(T(key))).ToArray())));
        body.Children.Add(Section("CannotConclude", Stack(findings.Select(item => T(item.NotEstablishedKey)).Distinct().Select(value => (Control)Label(value)).ToArray())));
        body.Children.Add(Expand("OtherTests", actions.Length <= 1 ? Label(T("NoOtherTests")) : Stack(actions.Skip(1).Select(key => (Control)Label(T(key))).ToArray())));
        var pattern = ViewModel.Result.Patterns.FirstOrDefault(item => item.IncidentIds.Contains(incident.Id));
        if (pattern is not null)
        {
            var occurrences = ViewModel.Result.Incidents.Where(item => pattern.IncidentIds.Contains(item.Id)).ToArray();
            body.Children.Add(Section("RecurringPattern", Stack(Label(ViewModel.Text.Format("RecurringCount", occurrences.Length)),
                Muted(ViewModel.Text.Format("FirstLatest", occurrences.Min(item => item.StartTimeUtc).ToLocalTime().ToString("g", ViewModel.Text.Culture), occurrences.Max(item => item.StartTimeUtc).ToLocalTime().ToString("g", ViewModel.Text.Culture))),
                Button("ViewOccurrences", ViewModel.ViewOccurrences, "ViewOccurrences"))));
        }
        body.Children.Add(ChangesSection(incident));
        body.Children.Add(Expand("SourceCoverage", Coverage(ViewModel.Result.Coverage), true));
        body.Children.Add(Expand("RawData", RawEvents(incident.SourceEvents)));
        return Scroll(body);
    }
    private StackPanel Section(string title, Control content)
    {
        var section = Stack(Label(T(title), TextRole.SectionTitle), content);
        section.Margin = new Thickness(0, D("primitive.space.2"), 0, 0);
        return section;
    }
    private string EvidenceText(Evidence item) => EvidencePresentation.Describe(item, ViewModel.Result.Coverage, ViewModel.Text);
    private string FriendlyEvent(NormalizedEvent item) => EvidencePresentation.FriendlyEvent(item, ViewModel.Text);
    private ListBox Timeline(Incident incident)
    {
        var list = new ListBox { ItemsSource = incident.SourceEvents.OrderBy(item => item.TimestampUtc).ToArray(), Height = 330, Name = "TimelineList" };
        list.ItemTemplate = new FuncDataTemplate<NormalizedEvent>((item, _) =>
        {
            if (item is null) return null;
            var relation = item.Id == incident.AnchorEvent.Id ? T("Anchor") :
                incident.Relations.FirstOrDefault(link => link.From == item.Id || link.To == item.Id) is { } link ? T("Relation" + link.Kind) : T("Context");
            var summary = Stack(Label(item.TimestampUtc.ToLocalTime().ToString("T", ViewModel.Text.Culture) + "  ·  " + FriendlyEvent(item), TextRole.RowTitle),
                Muted(relation + " · " + T("SourceType" + item.SourceType)));
            return new Expander { Header = summary, Content = RawEvent(item), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        });
        return list;
    }
    private ListBox RawEvents(IEnumerable<NormalizedEvent> events)
    {
        var list = new ListBox { ItemsSource = events, Height = 300 };
        list.ItemTemplate = new FuncDataTemplate<NormalizedEvent>((item, _) => item is null ? null : new Expander { Header = item.Provider + " / " + item.EventId, Content = RawEvent(item), HorizontalAlignment = HorizontalAlignment.Stretch });
        return list;
    }
    private StackPanel RawEvent(NormalizedEvent item)
    {
        var technical = string.Join(Environment.NewLine, new[] { "Provider: " + item.Provider, "Channel: " + item.Channel, "Event ID: " + item.EventId,
            "Record ID: " + item.Field("OriginalRecordId"), "UTC: " + item.TimestampUtc.ToString("O"), T("LocalTime") + ": " + item.TimestampUtc.ToLocalTime().ToString("O") }
            .Concat(item.Fields.Select(pair => pair.Key + ": " + pair.Value)));
        var box = new TextBox { Text = technical, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap, MaxHeight = 220 };
        var xml = new TextBox { Text = item.RawData ?? T("RawUnavailable"), IsReadOnly = true, AcceptsReturn = true, MaxHeight = 240 };
        return Stack(box, Expand("RawXml", xml));
    }

    private StackPanel ChangesSection(Incident incident)
    {
        if (incident.ChangeContext is not { } context)
            return Section("ChangesNearFirst", Muted(T("ChangesNotExamined")));
        var content = new List<Control>
        {
            Label(T("ChangesDisclaimer")),
            Label(ViewModel.Text.Format("FirstObservedValue", context.FirstObservedUtc.ToLocalTime().ToString("g", ViewModel.Text.Culture), T("FirstObservedBasis" + context.Basis))),
            Muted(T(context.IsRecurring ? "ChangesRecurring" : "ChangesNotRecurring"))
        };
        var changes = incident.RelatedChanges.OrderBy(item => item.Relevance).ThenBy(item => item.OffsetFromFirstObservation.Duration()).ToArray();
        var visible = changes.Where(item => item.Relevance != ContextualRelevance.Low).ToArray();
        if (visible.Length == 0) content.Add(Muted(T("NoRelevantChanges")));
        foreach (var related in visible) content.Add(ChangeRow(related));
        var low = changes.Where(item => item.Relevance == ContextualRelevance.Low).ToArray();
        if (low.Length > 0) content.Add(Expand("TechnicalDetails", Stack(low.Select(ChangeRow).ToArray())));
        if (context.Coverage.Count == 0 || context.Coverage.Any(item => item.State != CoverageState.Complete))
            content.Add(Muted(T("ChangesCoverageIncomplete")));
        foreach (var source in context.Coverage)
            content.Add(Muted(T(source.Channel ?? "ChangeSourceLocalHistory") + ": " + T("Coverage" + source.State)));
        if (context.Coverage.Count > 0) content.Add(Expand("SourceCoverage", Stack(context.Coverage.Select(c => (Control)Label(ChangePresentation.CoverageText(c, ViewModel.Text))).ToArray())));
        if (context.TotalChangeCount > changes.Length) content.Add(Muted(ViewModel.Text.Format("ChangesSummaryLimited", changes.Length, context.TotalChangeCount)));
        content.Add(Muted(T("ChangesCoverageHelp")));
        return Section("ChangesNearFirst", Stack(content.ToArray()));
    }

    private Control ChangeRow(RelatedSystemChange related)
    {
        return Stack(Label(ChangePresentation.RowText(related, ViewModel.Text)),
            Expand("TechnicalDetails", Muted(ChangePresentation.Details(related, ViewModel.Text))));
    }
}
