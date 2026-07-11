using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="ActionEvent" /> objects (incidents/observations).</summary>
public sealed class ActionEventMerger : ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.ActionEvent;

    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.ActionEvent?.Identity;
    }

    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.ActionEvent?.ReportingTime;
    }

    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.ActionEvent;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var actionEvent = current?.ActionEvent?.Clone() ?? new ActionEvent
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        actionEvent.ExpiryTime = PropertyMerge.Time(actionEvent.ExpiryTime, u.ExpiryTime, meta);
        actionEvent.Location = PropertyMerge.Location(actionEvent.Location, u.Location, meta);
        actionEvent.Name = PropertyMerge.String(actionEvent.Name, u.Name, meta);
        actionEvent.AdditionalInformation =
            PropertyMerge.String(actionEvent.AdditionalInformation, u.AdditionalInformation, meta);
        actionEvent.ActionEventType = PropertyMerge.ActionEvent(actionEvent.ActionEventType, u.ActionEventType, meta);
        actionEvent.LocationOfReporter =
            PropertyMerge.Location(actionEvent.LocationOfReporter, u.LocationOfReporter, meta);
        actionEvent.ThreatLevel = PropertyMerge.Int(actionEvent.ThreatLevel, u.ThreatLevel, meta);
        actionEvent.DetectionDescription =
            PropertyMerge.String(actionEvent.DetectionDescription, u.DetectionDescription, meta);
        actionEvent.Dimension = PropertyMerge.Dimension(actionEvent.Dimension, u.Dimension, meta);
        PropertyMerge.ForeignKey(actionEvent.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            ActionEvent = actionEvent,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
