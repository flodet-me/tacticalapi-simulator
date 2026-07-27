using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
///     Implements the update semantics of the TacticalAPI data properties:
///     an omitted (null) UpdateProperty leaves the stored value untouched, a
///     present UpdateProperty replaces the content (which itself may be null to
///     clear the value) - but only if the update's reporting time is strictly
///     newer than the property's own CreationMetaData.CreationTime, AND the
///     content actually differs from what's already stored. A same-time,
///     older, or no-op (identical content) update leaves the property exactly
///     as it is - no new CreationMetaData, no new instance - (last-write-wins,
///     per property rather than per whole object). Each written property gets
///     fresh CreationMetaData from the update's reporter and reporting time.
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

    /// <summary>
    ///     True when <paramref name="existing" /> is set and is not strictly
    ///     older than <paramref name="incoming" /> - i.e. the incoming update
    ///     must be discarded because it is not actually newer.
    /// </summary>
    private static bool IsStale(Timestamp? existing, Timestamp? incoming)
    {
        return existing is not null && incoming is not null && incoming.CompareTo(existing) <= 0;
    }

    internal static DataPropertyString? String(DataPropertyString? current, UpdatePropertyString? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyString { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyInt? Int(DataPropertyInt? current, UpdatePropertyInt? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyInt { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyTimestamp? Time(DataPropertyTimestamp? current, UpdatePropertyTimestamp? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyTimestamp { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyLocation? Location(DataPropertyLocation? current, UpdatePropertyLocation? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyLocation { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertySymbolIdentifier? SymbolId(DataPropertySymbolIdentifier? current,
        UpdatePropertySymbolIdentifier? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertySymbolIdentifier { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertyReinforcement? Reinforcement(DataPropertyReinforcement? current,
        UpdatePropertyReinforcement? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyReinforcement { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyDimension? Dimension(DataPropertyDimension? current, UpdatePropertyDimension? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && current.X == update.X && current.Y == update.Y && current.Z == update.Z)
            return current;

        return new DataPropertyDimension { CreationMetaData = meta, X = update.X, Y = update.Y, Z = update.Z };
    }

    internal static DataPropertyByteArray? Bytes(DataPropertyByteArray? current, UpdatePropertyByteArray? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content) && Equals(current.Type, update.Type))
            return current;

        return new DataPropertyByteArray { CreationMetaData = meta, Content = update.Content, Type = update.Type };
    }

    internal static DataPropertyMessageCategory? MessageCategory(DataPropertyMessageCategory? current,
        UpdatePropertyMessageCategory? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyMessageCategory { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyMessagePrecedence? MessagePrecedence(DataPropertyMessagePrecedence? current,
        UpdatePropertyMessagePrecedence? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyMessagePrecedence { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyColor? Color(DataPropertyColor? current, UpdatePropertyColor? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyColor { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertyActionTask? ActionTask(DataPropertyActionTask? current,
        UpdatePropertyActionTask? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyActionTask { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionTaskStatus? ActionTaskStatus(DataPropertyActionTaskStatus? current,
        UpdatePropertyActionTaskStatus? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyActionTaskStatus { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionTaskPriorityCode? ActionTaskPriority(DataPropertyActionTaskPriorityCode? current,
        UpdatePropertyActionTaskPriorityCode? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyActionTaskPriorityCode { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionEvent? ActionEvent(DataPropertyActionEvent? current,
        UpdatePropertyActionEvent? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyActionEvent { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyUnitDesignation? UnitDesignation(DataPropertyUnitDesignation? current,
        UpdatePropertyUnitDesignation? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyUnitDesignation { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyLineStyle? LineStyle(DataPropertyLineStyle? current, UpdatePropertyLineStyle? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyLineStyle { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyRouteType? RouteType(DataPropertyRouteType? current, UpdatePropertyRouteType? update,
        CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && Equals(current.Content, update.Content))
            return current;

        return new DataPropertyRouteType { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyReferences? References(DataPropertyReferences? current,
        UpdatePropertyReferences? update, CreationMetaData meta)
    {
        if (update is null
            || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)
            || current is not null && current.Contents.SequenceEqual(update.Contents))
            return current;

        var result = new DataPropertyReferences { CreationMetaData = meta };
        foreach (var identity in update.Contents) result.Contents.Add(identity.Clone());

        return result;
    }

    /// <summary>
    ///     Overlay content: the update carries nested UpdateSituationObjects; the
    ///     stored property carries full SituationObjects. Each nested update is
    ///     materialized as a fresh object via the shared merge logic.
    /// </summary>
    internal static DataPropertySituationObjects? SituationObjects(
        DataPropertySituationObjects? current,
        UpdatePropertySituationObjects? update,
        CreationMetaData meta,
        Func<UpdateSituationObject, SituationObject?> materialize)
    {
        if (update is null || IsStale(current?.CreationMetaData?.CreationTime, meta.CreationTime)) return current;

        var materialized = new List<SituationObject>();
        foreach (var nested in update.Contents)
            if (materialize(nested) is { } obj)
                materialized.Add(obj);

        if (current is not null && current.Contents.SequenceEqual(materialized)) return current;

        var result = new DataPropertySituationObjects { CreationMetaData = meta };
        foreach (var obj in materialized) result.Contents.Add(obj);

        return result;
    }

    /// <summary>
    ///     The update model carries a single foreign key (identity + source);
    ///     the stored model keeps a dictionary keyed by source. Each source's
    ///     entry independently applies the same newer-than-current and
    ///     content-changed rules as the scalar properties above.
    /// </summary>
    internal static void ForeignKey(IDictionary<string, DataPropertyIdentity> target, UpdatePropertyIdentity? update,
        CreationMetaData meta)
    {
        if (update is null) return;

        var key = update.Source ?? string.Empty;
        if (target.TryGetValue(key, out var existing))
        {
            if (IsStale(existing.CreationMetaData?.CreationTime, meta.CreationTime)) return;
            if (Equals(existing.Content, update.Content)) return;
        }

        target[key] = new DataPropertyIdentity
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
