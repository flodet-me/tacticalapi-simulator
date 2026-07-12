using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="VoiceMessageDocument" /> objects.</summary>
public sealed class VoiceMessageDocumentMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.VoiceMessageDocument;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.VoiceMessageDocument?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.VoiceMessageDocument?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.VoiceMessageDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.VoiceMessageDocument?.Clone() ?? new VoiceMessageDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Location = PropertyMerge.Location(doc.Location, u.Location, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.SoundFile = PropertyMerge.Bytes(doc.SoundFile, u.SoundFile, meta);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            VoiceMessageDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
