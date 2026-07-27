using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="OrganizationUnit" /> objects (ORBAT).</summary>
public sealed class OrganizationUnitMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.OrganizationUnit;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.OrganizationUnit?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.OrganizationUnit?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.OrganizationUnit;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var unit = current?.OrganizationUnit?.Clone() ?? new OrganizationUnit
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        unit.ExpiryTime = PropertyMerge.Time(unit.ExpiryTime, u.ExpiryTime, meta);
        unit.Name = PropertyMerge.String(unit.Name, u.Name, meta);
        unit.AdditionalInformation = PropertyMerge.String(unit.AdditionalInformation, u.AdditionalInformation, meta);
        unit.SymbolIdentifier = PropertyMerge.SymbolId(unit.SymbolIdentifier, u.SymbolIdentifier, meta);
        unit.HigherFormation = PropertyMerge.String(unit.HigherFormation, u.HigherFormation, meta);
        unit.OrganizationUnitColor = PropertyMerge.Color(unit.OrganizationUnitColor, u.OrganizationUnitColor, meta);
        unit.UnitDesignation = PropertyMerge.UnitDesignation(unit.UnitDesignation, u.UnitDesignation, meta);
        unit.SubordinatedOrganizationUnitCollection = PropertyMerge.References(
            unit.SubordinatedOrganizationUnitCollection, u.SubordinatedOrganizationUnitCollection, meta);
        PropertyMerge.ForeignKey(unit.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            OrganizationUnit = unit,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
