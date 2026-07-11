using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="SketchDocument" /> objects.</summary>
public sealed class SketchDocumentMerger : ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.SketchDocument;

    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.SketchDocument?.Identity;
    }

    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.SketchDocument?.ReportingTime;
    }

    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.SketchDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.SketchDocument?.Clone() ?? new SketchDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Location = PropertyMerge.Location(doc.Location, u.Location, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            SketchDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
