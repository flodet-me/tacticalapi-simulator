using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="TextDocument" /> objects (messaging).</summary>
public sealed class TextDocumentMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.TextDocument;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.TextDocument?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.TextDocument?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.TextDocument;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var doc = current?.TextDocument?.Clone() ?? new TextDocument
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        doc.ExpiryTime = PropertyMerge.Time(doc.ExpiryTime, u.ExpiryTime, meta);
        doc.Location = PropertyMerge.Location(doc.Location, u.Location, meta);
        doc.Name = PropertyMerge.String(doc.Name, u.Name, meta);
        doc.AdditionalInformation = PropertyMerge.String(doc.AdditionalInformation, u.AdditionalInformation, meta);
        doc.FreeHandDrawingData = PropertyMerge.Bytes(doc.FreeHandDrawingData, u.FreeHandDrawingData, meta);
        doc.Content = PropertyMerge.String(doc.Content, u.Content, meta);
        doc.PlainContent = PropertyMerge.String(doc.PlainContent, u.PlainContent, meta);
        doc.MessageCategory = PropertyMerge.MessageCategory(doc.MessageCategory, u.MessageCategory, meta);
        doc.MessagePrecedence = PropertyMerge.MessagePrecedence(doc.MessagePrecedence, u.MessagePrecedence, meta);
        PropertyMerge.ForeignKey(doc.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            TextDocument = doc,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
