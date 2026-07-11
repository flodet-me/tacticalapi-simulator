using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
///     Implements the update semantics of the TacticalAPI data properties:
///     an omitted (null) UpdateProperty leaves the stored value untouched, a
///     present UpdateProperty replaces the content (which itself may be null to
///     clear the value). Each written property gets fresh CreationMetaData from
///     the update's reporter and reporting time.
/// </summary>
internal static class PropertyMerge
{
    internal static CreationMetaData Meta(Identity? reporter, Timestamp? reportingTime)
    {
        return new CreationMetaData
        {
            CreationTime = reportingTime,
            CreatorIdentity = reporter
        };
    }

    internal static DataPropertyString? String(DataPropertyString? current, UpdatePropertyString? update,
        CreationMetaData meta)
    {
        return update is null ? current : new DataPropertyString { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyInt? Int(DataPropertyInt? current, UpdatePropertyInt? update, CreationMetaData meta)
    {
        return update is null ? current : new DataPropertyInt { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyTimestamp? Time(DataPropertyTimestamp? current, UpdatePropertyTimestamp? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyTimestamp { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyLocation? Location(DataPropertyLocation? current, UpdatePropertyLocation? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyLocation { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertySymbolIdentifier? SymbolId(DataPropertySymbolIdentifier? current,
        UpdatePropertySymbolIdentifier? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertySymbolIdentifier { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertyReinforcement? Reinforcement(DataPropertyReinforcement? current,
        UpdatePropertyReinforcement? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyReinforcement { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyDimension? Dimension(DataPropertyDimension? current, UpdatePropertyDimension? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyDimension { CreationMetaData = meta, X = update.X, Y = update.Y, Z = update.Z };
    }

    internal static DataPropertyByteArray? Bytes(DataPropertyByteArray? current, UpdatePropertyByteArray? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyByteArray { CreationMetaData = meta, Content = update.Content, Type = update.Type };
    }

    internal static DataPropertyMessageCategory? MessageCategory(DataPropertyMessageCategory? current,
        UpdatePropertyMessageCategory? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyMessageCategory { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyMessagePrecedence? MessagePrecedence(DataPropertyMessagePrecedence? current,
        UpdatePropertyMessagePrecedence? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyMessagePrecedence { CreationMetaData = meta, Content = update.Content };
    }

    /// <summary>
    ///     The update model carries a single foreign key (identity + source);
    ///     the stored model keeps a dictionary keyed by source.
    /// </summary>
    internal static void ForeignKey(IDictionary<string, DataPropertyIdentity> target, UpdatePropertyIdentity? update,
        CreationMetaData meta)
    {
        if (update is null) return;

        target[update.Source ?? string.Empty] = new DataPropertyIdentity
        {
            CreationMetaData = meta,
            Content = update.Content?.Clone(),
            Source = update.Source
        };
    }

    internal static DataPropertyBool Deleted(bool isDeleted, CreationMetaData meta)
    {
        return new DataPropertyBool
        {
            CreationMetaData = meta,
            Content = isDeleted
        };
    }
}
