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

    internal static DataPropertyColor? Color(DataPropertyColor? current, UpdatePropertyColor? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyColor { CreationMetaData = meta, Content = update.Content?.Clone() };
    }

    internal static DataPropertyActionTask? ActionTask(DataPropertyActionTask? current,
        UpdatePropertyActionTask? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyActionTask { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionTaskStatus? ActionTaskStatus(DataPropertyActionTaskStatus? current,
        UpdatePropertyActionTaskStatus? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyActionTaskStatus { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionTaskPriorityCode? ActionTaskPriority(DataPropertyActionTaskPriorityCode? current,
        UpdatePropertyActionTaskPriorityCode? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyActionTaskPriorityCode { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyActionEvent? ActionEvent(DataPropertyActionEvent? current,
        UpdatePropertyActionEvent? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyActionEvent { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyUnitDesignation? UnitDesignation(DataPropertyUnitDesignation? current,
        UpdatePropertyUnitDesignation? update, CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyUnitDesignation { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyLineStyle? LineStyle(DataPropertyLineStyle? current, UpdatePropertyLineStyle? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyLineStyle { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyRouteType? RouteType(DataPropertyRouteType? current, UpdatePropertyRouteType? update,
        CreationMetaData meta)
    {
        return update is null
            ? current
            : new DataPropertyRouteType { CreationMetaData = meta, Content = update.Content };
    }

    internal static DataPropertyReferences? References(DataPropertyReferences? current,
        UpdatePropertyReferences? update, CreationMetaData meta)
    {
        if (update is null) return current;

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
        if (update is null) return current;

        var result = new DataPropertySituationObjects { CreationMetaData = meta };
        foreach (var nested in update.Contents)
            if (materialize(nested) is { } obj)
                result.Contents.Add(obj);

        return result;
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
