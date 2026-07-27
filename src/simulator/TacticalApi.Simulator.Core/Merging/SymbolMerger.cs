using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
///     Merge logic for <see cref="Symbol" /> objects (map symbols / tracks) -
///     the primary type for simulated air and naval tracks.
/// </summary>
public sealed class SymbolMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.Symbol;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.Symbol?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.Symbol?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.Symbol;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        // Copy-on-write: never mutate the currently published instance.
        var symbol = current?.Symbol?.Clone() ?? new Symbol
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        symbol.ExpiryTime = PropertyMerge.Time(symbol.ExpiryTime, u.ExpiryTime, meta);
        symbol.Location = PropertyMerge.Location(symbol.Location, u.Location, meta);
        symbol.Name = PropertyMerge.String(symbol.Name, u.Name, meta);
        symbol.AdditionalInformation =
            PropertyMerge.String(symbol.AdditionalInformation, u.AdditionalInformation, meta);
        symbol.SymbolIdentifier = PropertyMerge.SymbolId(symbol.SymbolIdentifier, u.SymbolIdentifier, meta);
        symbol.HigherFormation = PropertyMerge.String(symbol.HigherFormation, u.HigherFormation, meta);
        symbol.Reinforcement = PropertyMerge.Reinforcement(symbol.Reinforcement, u.Reinforcement, meta);
        symbol.StartTime = PropertyMerge.Time(symbol.StartTime, u.StartTime, meta);
        symbol.EndTime = PropertyMerge.Time(symbol.EndTime, u.EndTime, meta);
        symbol.EquipmentType = PropertyMerge.String(symbol.EquipmentType, u.EquipmentType, meta);
        symbol.Quantity = PropertyMerge.Int(symbol.Quantity, u.Quantity, meta);
        symbol.StaffComment = PropertyMerge.String(symbol.StaffComment, u.StaffComment, meta);
        symbol.Dimension = PropertyMerge.Dimension(symbol.Dimension, u.Dimension, meta);
        PropertyMerge.ForeignKey(symbol.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            Symbol = symbol,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
