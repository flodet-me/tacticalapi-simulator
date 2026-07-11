using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
///     Merge logic for <see cref="OverlayDocument" /> objects. The nested overlay
///     content (UpdateSituationObjects) is materialized into full SituationObjects
///     using the same merge logic as top-level objects.
/// </summary>
public sealed class OverlayDocumentMerger : ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.OverlayDocument;

    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.OverlayDocument?.Identity;
    }

    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.OverlayDocument?.ReportingTime;
    }

    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.OverlayDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.OverlayDocument?.Clone() ?? new OverlayDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.Tag = PropertyMerge.String(doc.Tag, u.Tag, meta);
        doc.OverlayData = PropertyMerge.SituationObjects(
            doc.OverlayData, u.OverlayData, meta, NestedObjectMaterializer.Materialize);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            OverlayDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
