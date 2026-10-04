using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Rules;

namespace FaultWitness.App;

/// <summary>Identity from the classified anchor only. Nearby context is never attributed to the incident.</summary>
public sealed record IncidentIdentity(string Type, string Subject, string Source, string EventIdentity)
{
    public static IncidentIdentity From(Incident incident, LocalizationService text)
    {
        var anchor = incident.AnchorEvent;
        var type = text.Get("Category" + incident.Category);
        var subject = string.Empty;
        if (incident.Category == IncidentCategory.Service)
        {
            var service = DiagnosticFacts.ServiceName(anchor);
            if (!string.IsNullOrWhiteSpace(service)) subject = text.Format("IncidentServiceValue", service);
        }
        if (subject.Length == 0 && !string.IsNullOrWhiteSpace(anchor.Process))
            subject = text.Format("IncidentProcessValue", DiagnosticFacts.FileName(anchor.Process));
        if (subject.Length == 0)
        {
            var component = DiagnosticFacts.Device(anchor);
            if (string.IsNullOrWhiteSpace(component)) component = anchor.Field("Component");
            if (!string.IsNullOrWhiteSpace(component)) subject = text.Format("IncidentComponentValue", component);
        }
        if (subject.Length == 0) subject = text.Get("IncidentIdentityUnknown");
        var source = text.Get("SourceType" + anchor.SourceType) + " — " + anchor.Provider;
        if (!string.IsNullOrWhiteSpace(anchor.Channel)) source += " · " + anchor.Channel;
        // WER files use a normalized report ID; do not describe it as a Windows Event Log Event ID.
        var eventIdentity = anchor.SourceType is SourceType.EventLog or SourceType.Imported &&
            anchor.Channel is "Application" or "System" ? text.Get("DetailEventId") + ": " + anchor.EventId : string.Empty;
        return new(type, subject, text.Get("ChangeSource") + ": " + source, eventIdentity);
    }
}
