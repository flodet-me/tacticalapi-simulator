using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="PictureDocument" /> objects.</summary>
public sealed class PictureDocumentMerger : ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.PictureDocument;

    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.PictureDocument?.Identity;
    }

    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.PictureDocument?.ReportingTime;
    }

    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.PictureDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.PictureDocument?.Clone() ?? new PictureDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Location = PropertyMerge.Location(doc.Location, u.Location, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.LowResPictureData = PropertyMerge.Bytes(doc.LowResPictureData, u.LowResPictureData, meta);
        doc.PictureData = PropertyMerge.Bytes(doc.PictureData, u.PictureData, meta);
        doc.DirectionOfView = PropertyMerge.Int(doc.DirectionOfView, u.DirectionOfView, meta);
        doc.FocalLength = PropertyMerge.Int(doc.FocalLength, u.FocalLength, meta);
        doc.FreeHandDrawingData = PropertyMerge.Bytes(doc.FreeHandDrawingData, u.FreeHandDrawingData, meta);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            PictureDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
