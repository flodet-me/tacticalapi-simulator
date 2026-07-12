using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="NatoMessageDocument" /> objects (MTF messages).</summary>
public sealed class NatoMessageDocumentMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.NatoMessageDocument;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.NatoMessageDocument?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.NatoMessageDocument?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.NatoMessageDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.NatoMessageDocument?.Clone() ?? new NatoMessageDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Location = PropertyMerge.Location(doc.Location, u.Location, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.MtfMessageData = PropertyMerge.String(doc.MtfMessageData, u.MtfMessageData, meta);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            NatoMessageDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
