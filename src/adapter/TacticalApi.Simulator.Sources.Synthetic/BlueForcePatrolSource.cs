using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline scenario for the two services a situation picture alone can't
///     exercise: a section ("BADGER") patrols a loop, reporting itself over
///     <c>BlueForceTracking</c>, while its leader's handheld GNSS feeds
///     <c>OwnPose</c>.
///     It is a blue force source rather than a simulation source on purpose. A blue
///     force is not a situation object with a friendly affiliation - it is a
///     friendly participant updating its own position on a keep-alive cadence, and
///     the contract gives it its own service, its own message and its own implicit
///     deletion rule. Emitting these as <c>Symbol</c>s too would produce the same
///     vehicle twice in a client's picture from two services that disagree about
///     what it is, so the existing scenarios keep the situation picture and this one
///     owns the blue force picture.
///     Three things here exist to be tested against rather than to look pretty:
///     <list type="bullet">
///         <item>
///             <c>mount_host</c> - the section alternates mounted and dismounted; while
///             mounted every rifleman reports the carrier as its host and rides its
///             position, which is the relationship a client's map has to collapse.
///         </item>
///         <item>
///             <c>is_leader</c>/<c>is_vehicle</c>/<c>is_unmanned</c> - all three kinds are
///             present at once, since <c>BlueForceType</c> allows several to be true.
///         </item>
///         <item>
///             GNSS dropout - the leader's position source goes silent at random, which
///             is exactly the case the contract describes ("because the user entered a
///             building"), and leaves the server's own staleness handling to show a
///             client the position going invalid without disappearing.
///         </item>
///     </list>
///     Symbol codes are illustrative MIL-STD-2525C, friend affiliation - the same
///     convention the convoy and NWS sources already use for symbology this contract
///     doesn't cover natively.
/// </summary>
public sealed class BlueForcePatrolSource(
    IOptionsMonitor<BlueForcePatrolOptions> options,
    TimeProvider timeProvider,
    ILogger<BlueForcePatrolSource> logger)
    : IBlueForceSource, IOwnPoseSource
{
    private const string InfantrySidc = "SFGPUCI--------";
    private const string CarrierSidc = "SFGPUCAAW------";
    private const string UasSidc = "SFAPMFQ--------";

    private const string VehicleId = "blueforce:patrol:carrier";
    private const string LeaderId = "blueforce:patrol:leader";
    private const string UasId = "blueforce:patrol:uas";

    private readonly Random _random = new(options.CurrentValue.Seed);
    private readonly DateTimeOffset _startedAt = timeProvider.GetUtcNow();

    // This source is registered against TWO hosted runners - one for
    // BlueForceTracking, one for OwnPose - deliberately sharing a single instance
    // so both agree on the patrol clock. They tick on their own timers, so every
    // mutable field here is reachable from two threads and lives behind this gate.
    // Today only ProducePositionAsync touches any of it, which is exactly why the
    // gate is worth having: a Random used from two threads returns garbage and can
    // corrupt its own state, and the next person to want a random anything in
    // ProduceBlueForcesAsync would introduce that silently. Everything else here is
    // a pure function of the options and the current time, and needs no gate.
    private readonly Lock _stateGate = new();

    private DateTimeOffset? _gnssOutageUntil;

    /// <inheritdoc/>
    public string Name => SimulationSourceName.FromSectionName(BlueForcePatrolOptions.SectionName);

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <summary>
    ///     Identity of the blue force that represents the reporting system itself.
    ///     The <c>own_blue_force</c> flag is set by the server, not by the update
    ///     (<c>UpdateBlueForce</c> has no such field), so this is what a Host's
    ///     <c>Simulator:BlueForce:OwnIdentity</c> should be pointed at for this
    ///     scenario to show an own force.
    /// </summary>
    public static string OwnBlueForceIdentity => LeaderId;

    /// <inheritdoc/>
    public Task<IReadOnlyList<UpdateBlueForce>> ProduceBlueForcesAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);

        var vehicle = VehiclePosition(o, now);
        var mounted = IsMounted(o, now);
        var vehicleIdentity = new Identity { StringIdentity = VehicleId };

        var updates = new List<UpdateBlueForce>(o.DismountCount + 3)
        {
            BlueForce(VehicleId, $"{o.Callsign} CARRIER", CarrierSidc, nowTs,
                vehicle, course: LoopCourse(o, now), speedMetersPerSecond: 6.0,
                type: new BlueForceType { IsVehicle = true }),

            // The UAS orbits ahead of the vehicle while the section is dismounted and
            // rides it while mounted, which is what makes its mount_host come and go
            // independently of the riflemen's.
            UasUpdate(o, now, nowTs, vehicle, mounted, vehicleIdentity)
        };

        // The leader and the riflemen: mounted they share the carrier's position and
        // name it as their host, dismounted they spread out on their own.
        updates.Add(BlueForce(LeaderId, $"{o.Callsign} 1 (leader)", InfantrySidc, nowTs,
            mounted ? vehicle : Dismounted(o, vehicle, 0),
            course: null, speedMetersPerSecond: mounted ? 6.0 : 1.3,
            type: new BlueForceType { IsLeader = true },
            mountHost: mounted ? vehicleIdentity : null));

        for (var i = 0; i < o.DismountCount; i++)
            updates.Add(BlueForce($"blueforce:patrol:rifleman:{i}", $"{o.Callsign} {i + 2}", InfantrySidc, nowTs,
                mounted ? vehicle : Dismounted(o, vehicle, i + 1),
                course: null, speedMetersPerSecond: mounted ? 6.0 : 1.3,
                type: new BlueForceType(),
                mountHost: mounted ? vehicleIdentity : null));

        logger.BlueForcesProduced(updates.Count, mounted);
        return Task.FromResult<IReadOnlyList<UpdateBlueForce>>(updates);
    }

    /// <inheritdoc/>
    public Task<UpdatePosition?> ProducePositionAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();

        var gnss = AdvanceGnss(o, now);

        // Logged outside the gate: a logging provider is somebody else's code, and
        // holding a lock across it is how an unrelated sink turns into a stall here.
        if (gnss.Recovered) logger.GnssRestored();
        if (gnss.WentOut) logger.GnssOutageStarted(o.GnssOutageDuration.TotalSeconds);

        // Reporting nothing rather than reporting a stale fix: the sensor is the
        // thing that has gone away, and it is the server's job to decide when the
        // last known position stops counting as current.
        if (!gnss.Reporting) return Task.FromResult<UpdatePosition?>(null);

        var vehicle = VehiclePosition(o, now);
        var leader = IsMounted(o, now) ? vehicle : Dismounted(o, vehicle, 0);

        return Task.FromResult<UpdatePosition?>(new UpdatePosition
        {
            SourceIdentifier = o.PositionSourceIdentifier,
            PointLocation = Point(leader, Timestamp.FromDateTimeOffset(now), null, 1.3)
        });
    }

    /// <summary>What one tick of the GNSS outage state machine did.</summary>
    /// <param name="Reporting">Whether a fix should be sent this cycle.</param>
    /// <param name="Recovered">Whether an outage ended on this tick.</param>
    /// <param name="WentOut">Whether an outage started on this tick.</param>
    private readonly record struct GnssTick(bool Reporting, bool Recovered, bool WentOut);

    /// <summary>
    ///     Advances the GNSS outage state machine by one cycle. The whole of this
    ///     source's mutable state is read and written here, under
    ///     <see cref="_stateGate" /> - see the field's comment for why that matters
    ///     in a class driven by two runners.
    /// </summary>
    private GnssTick AdvanceGnss(BlueForcePatrolOptions o, DateTimeOffset now)
    {
        lock (_stateGate)
        {
            if (_gnssOutageUntil is { } until && now < until) return new GnssTick(false, false, false);

            var recovered = _gnssOutageUntil is not null;
            _gnssOutageUntil = null;

            if (_random.NextDouble() < o.GnssOutageProbability)
            {
                _gnssOutageUntil = now + o.GnssOutageDuration;
                return new GnssTick(false, recovered, true);
            }

            return new GnssTick(true, recovered, false);
        }
    }

    /// <summary>Where the carrier is on its loop right now.</summary>
    private (double Lat, double Lon) VehiclePosition(BlueForcePatrolOptions o, DateTimeOffset now)
    {
        var bearing = LoopBearing(o, now);
        return GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, bearing, o.PatrolRadiusM);
    }

    private double LoopBearing(BlueForcePatrolOptions o, DateTimeOffset now)
    {
        var lapFraction = (now - _startedAt).TotalSeconds / o.LapDuration.TotalSeconds % 1.0;
        return lapFraction * 360.0;
    }

    /// <summary>Heading along the loop - tangential to the radius, so 90 degrees ahead of it.</summary>
    private double LoopCourse(BlueForcePatrolOptions o, DateTimeOffset now)
    {
        return (LoopBearing(o, now) + 90.0) % 360.0;
    }

    /// <summary>
    ///     Whether the section is currently aboard the carrier. A plain repeating
    ///     mounted/dismounted cycle rather than anything scripted: the point is that
    ///     mount_host appears and disappears often enough for a client to be caught
    ///     out by it, not that the pattern is interesting.
    /// </summary>
    private bool IsMounted(BlueForcePatrolOptions o, DateTimeOffset now)
    {
        var cycle = o.MountedPhaseDuration + o.DismountedPhaseDuration;
        var elapsed = (now - _startedAt).TotalSeconds % cycle.TotalSeconds;
        return elapsed < o.MountedPhaseDuration.TotalSeconds;
    }

    /// <summary>Fans the dismounts out around the carrier at a fixed, deterministic spread.</summary>
    private static (double Lat, double Lon) Dismounted(
        BlueForcePatrolOptions o, (double Lat, double Lon) vehicle, int index)
    {
        var bearing = index * (360.0 / Math.Max(1, o.DismountCount + 1));
        return GeoMath.Destination(vehicle.Lat, vehicle.Lon, bearing, o.DismountSpreadM);
    }

    private static UpdateBlueForce UasUpdate(
        BlueForcePatrolOptions o, DateTimeOffset now, Timestamp nowTs,
        (double Lat, double Lon) vehicle, bool mounted, Identity vehicleIdentity)
    {
        // Stowed on the carrier while mounted; orbiting a few hundred meters ahead
        // of it, at altitude, once the section is on the ground.
        var orbitBearing = (now - DateTimeOffset.UnixEpoch).TotalSeconds * 12.0 % 360.0;
        var position = mounted
            ? vehicle
            : GeoMath.Destination(vehicle.Lat, vehicle.Lon, orbitBearing, o.DismountSpreadM * 3);

        return BlueForce(UasId, $"{o.Callsign} UAS", UasSidc, nowTs, position,
            course: mounted ? null : (orbitBearing + 90.0) % 360.0,
            speedMetersPerSecond: mounted ? 6.0 : 14.0,
            type: new BlueForceType { IsUnmanned = true },
            mountHost: mounted ? vehicleIdentity : null,
            altitudeMeters: mounted ? null : 120);
    }

    private static UpdateBlueForce BlueForce(
        string id, string callsign, string sidc, Timestamp nowTs,
        (double Lat, double Lon) position, double? course, double? speedMetersPerSecond,
        BlueForceType type, Identity? mountHost = null, double? altitudeMeters = null)
    {
        var update = new UpdateBlueForce
        {
            Identity = new Identity { StringIdentity = id },

            // The keep-alive the contract's 30s rule is about: this call is what
            // stops the blue force being implicitly deleted, and last_contact_time
            // is the only ordering the message carries.
            LastContactTime = nowTs,
            Callsign = callsign,
            Symbol = new SymbolIdentifier
            {
                SymbolCatalog = SymbolCatalog.Mil2525C,
                StringIdentifier = sidc
            },
            BlueForceType = type,
            PointLocation = Point(position, nowTs, altitudeMeters, speedMetersPerSecond, course)
        };

        if (mountHost is not null) update.MountHost = mountHost;
        return update;
    }

    private static Point Point(
        (double Lat, double Lon) position, Timestamp nowTs,
        double? altitudeMeters = null, double? speedMetersPerSecond = null, double? course = null)
    {
        return new Point
        {
            LocationTime = nowTs,
            GeoPoint = new GeoPoint
            {
                LatitudeCoordinate = position.Lat,
                LongitudeCoordinate = position.Lon,
                VerticalDistance = altitudeMeters,
                VerticalDistanceReferenceCode = altitudeMeters is null
                    ? VerticalDistanceReferenceCode.Unspecified
                    : VerticalDistanceReferenceCode.MeanSeaLevel,
                MeasurementCode = MeasurementCode.Gps
            },
            Course = course,
            Speed = speedMetersPerSecond
        };
    }
}
