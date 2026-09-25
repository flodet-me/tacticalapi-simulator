using System.Globalization;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Identities;

namespace TacticalApi.Simulator.Host.Web;

/// <summary>
///     Flattens blue force snapshots into <see cref="MapBlueForce" />s for the
///     read-only map GUI (<c>/api/blueforces</c>).
///     Deliberately a separate mapper and a separate endpoint from
///     <see cref="SituationObjectMapper" />: blue forces come from a different
///     service, carry different fields (a callsign, a mount host, the own-force
///     flag) and mean something different - a friendly participant reporting
///     itself, not an object someone reported about. Folding them into the
///     situation layer would hide exactly that distinction.
/// </summary>
public static class BlueForceMapper
{
    /// <summary>Maps every blue force in <paramref name="blueForces" /> for display.</summary>
    public static IReadOnlyList<MapBlueForce> Map(IReadOnlyList<BlueForce> blueForces)
    {
        ArgumentNullException.ThrowIfNull(blueForces);

        var result = new List<MapBlueForce>(blueForces.Count);
        foreach (var blueForce in blueForces)
        {
            var id = IdentityKey.TryCreate(blueForce.Identity);
            if (id is null) continue;

            var point = blueForce.PointLocation?.GeoPoint;
            result.Add(new MapBlueForce(
                id,
                blueForce.Callsign,
                point is null
                    ? null
                    : new MapPoint(point.LatitudeCoordinate, point.LongitudeCoordinate),
                blueForce.PointLocation?.Course,
                blueForce.PointLocation?.Speed,
                blueForce.OwnBlueForce,
                blueForce.BlueForceType?.IsVehicle ?? false,
                blueForce.BlueForceType?.IsUnmanned ?? false,
                blueForce.BlueForceType?.IsLeader ?? false,
                IdentityKey.TryCreate(blueForce.MountHost),
                IdentityKey.TryCreate(blueForce.AssociatedOrganizationUnitIdentity),
                blueForce.LastContactTime?.ToDateTimeOffset()
                    .ToString("O", CultureInfo.InvariantCulture),
                SituationObjectMapper.SymbolIdentifierText(blueForce.Symbol)));
        }

        return result;
    }
}
