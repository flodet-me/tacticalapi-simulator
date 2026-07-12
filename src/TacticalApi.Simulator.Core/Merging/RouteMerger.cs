using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Merge logic for <see cref="Route" /> objects.</summary>
public sealed class RouteMerger : ISituationObjectMerger
{
    /// <inheritdoc/>
    public UpdateSituationObject.TypeOneofCase HandledCase => UpdateSituationObject.TypeOneofCase.Route;

    /// <inheritdoc/>
    public Identity? GetIdentity(UpdateSituationObject update)
    {
        return update.Route?.Identity;
    }

    /// <inheritdoc/>
    public Timestamp? GetReportingTime(UpdateSituationObject update)
    {
        return update.Route?.ReportingTime;
    }

    /// <inheritdoc/>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update)
    {
        var u = update.Route;
        var meta = PropertyMerge.Meta(u.Reporter, u.ReportingTime);

        var route = current?.Route?.Clone() ?? new Route
        {
            Identity = u.Identity?.Clone(),
            CreationMetaData = meta
        };

        route.ExpiryTime = PropertyMerge.Time(route.ExpiryTime, u.ExpiryTime, meta);
        route.Location = PropertyMerge.Location(route.Location, u.Location, meta);
        route.Name = PropertyMerge.String(route.Name, u.Name, meta);
        route.AdditionalInformation = PropertyMerge.String(route.AdditionalInformation, u.AdditionalInformation, meta);
        route.MarchSpeed = PropertyMerge.Int(route.MarchSpeed, u.MarchSpeed, meta);
        route.LineColor = PropertyMerge.Color(route.LineColor, u.LineColor, meta);
        route.LineWidth = PropertyMerge.Int(route.LineWidth, u.LineWidth, meta);
        route.LineStyle = PropertyMerge.LineStyle(route.LineStyle, u.LineStyle, meta);
        route.RouteType = PropertyMerge.RouteType(route.RouteType, u.RouteType, meta);
        PropertyMerge.ForeignKey(route.ForeignKeys, u.ForeignKey, meta);

        return new SituationObject
        {
            Route = route,
            IsDeleted = current?.IsDeleted ?? PropertyMerge.Deleted(false, meta)
        };
    }
}
