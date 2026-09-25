using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline scenario: a logistics convoy ("TRIREME") shuttles back and forth along a supply
///     route between two points, escorted by gun trucks. Three scripted high-risk zones along the
///     route (a culvert, a market chokepoint, an underpass) carry an elevated ambush probability;
///     everywhere else the baseline probability is low. When an ambush triggers, a hostile element
///     is spawned and the engagement is resolved with <see cref="LanchesterModel" /> rather than a
///     coin flip, casualties are tracked per vehicle, and - if there are friendly casualties - a
///     CASEVAC <c>ActionTask</c> is raised alongside a SALUTE-format contact report.
///     The serial itself is reported over <c>BlueForceTracking</c>, not as situation objects.
///     That is what the convoy's vehicles are: friendly participants sending their own position on
///     a keep-alive cadence. Everything the convoy reports <i>about</i> - the route it is driving,
///     the ambush that hits it, the hostiles it sees, the CASEVAC it requests, the SALUTE it sends
///     up - stays on the <c>Situation</c> service, which is the other half of the same distinction.
///     Emitting the gun trucks on both would put the same vehicle into a client's picture twice
///     from two services that disagree about what it is.
///     Friendly symbols use MIL-STD-2525C's "friend" affiliation (2nd SIDC character <c>F</c>);
///     the ambush element uses "hostile" (<c>H</c>) - same illustrative-code convention the base
///     synthetic scenario and the NWS source already use for symbology this contract doesn't cover
///     natively.
/// </summary>
public sealed class ConvoyEscortSource(
    IOptionsMonitor<ConvoyEscortOptions> options,
    TimeProvider timeProvider,
    ILogger<ConvoyEscortSource> logger)
    : ISimulationSource, IBlueForceSource
{
    private const string RouteName = "Route CONDOR";
    private const string ConvoyCallsign = "TRIREME";

    private readonly Random _random = new(options.CurrentValue.Seed);
    private readonly RiskZone[] _riskZones = BuildRiskZones(options.CurrentValue);
    private readonly Dictionary<int, int> _casualtiesByVehicleIndex = new();

    // Two runners drive this one instance - the situation picture and the blue force
    // picture are separate services on separate schedules - so everything below is
    // reachable from two threads. ProduceAsync advances the scenario; the blue force
    // side only reads, so the leg can never be stepped twice in one cycle.
    private readonly Lock _stateGate = new();

    private DateTimeOffset _legStartTime = timeProvider.GetUtcNow();
    private bool _headingToEnd = true;
    private DateTimeOffset? _contactCooldownUntil;
    private int _eventCounter;
    private string _latestSalute = "No significant activity.";

    /// <inheritdoc/>
    public string Name => SimulationSourceName.FromSectionName(ConvoyEscortOptions.SectionName);

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    private sealed record RiskZone(string Name, string Description, double Lat, double Lon);

    /// <inheritdoc/>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);
        var reporter = new Identity { StringIdentity = o.ReporterId };

        List<UpdateSituationObject> updates;
        UpdateActionEvent? raisedContact;

        lock (_stateGate)
        {
            var fraction = AdvanceLeg(o, now);
            var (from, to) = LegEndpoints(o);
            var leadPosition = Lerp(from, to, Math.Clamp(fraction, 0, 1));

            updates = [SupplyRoute(reporter, nowTs, o)];
            raisedContact = TryRaiseAmbush(o, reporter, now, nowTs, from, to, leadPosition, updates);
            updates.Add(SaluteReport(reporter, nowTs, now));
        }

        // Logged outside the gate: a logging provider is somebody else's code, and
        // holding a lock across it is how an unrelated sink turns into a stall here.
        if (raisedContact is not null)
            logger.IncidentRaised(raisedContact.Identity.StringIdentity,
                raisedContact.Name.Content, raisedContact.ThreatLevel.Content ?? 0);

        logger.ScenarioCycleProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    /// <summary>
    ///     Rolls for an ambush and, if one triggers, appends the contact event, the
    ///     hostile element and any CASEVAC request to <paramref name="updates" />.
    ///     Returns the contact event for logging, or null if nothing happened.
    ///     Callers hold <see cref="_stateGate" />.
    /// </summary>
    private UpdateActionEvent? TryRaiseAmbush(
        ConvoyEscortOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs,
        (double Lat, double Lon) from, (double Lat, double Lon) to, (double Lat, double Lon) leadPosition,
        List<UpdateSituationObject> updates)
    {
        var nearestZone = _riskZones
            .Select(z => (Zone: z, DistanceM: GeoMath.DistanceMeters(leadPosition.Lat, leadPosition.Lon, z.Lat, z.Lon)))
            .OrderBy(z => z.DistanceM)
            .FirstOrDefault();

        var inRiskZone = nearestZone.Zone is not null && nearestZone.DistanceM <= o.RiskZoneRadiusM;
        var ambushProbability = inRiskZone ? o.BaseAmbushProbability * o.RiskZoneMultiplier : o.BaseAmbushProbability;
        var cooldownElapsed = _contactCooldownUntil is null || now >= _contactCooldownUntil;

        if (!cooldownElapsed || _random.NextDouble() >= ambushProbability) return null;

        _contactCooldownUntil = now + o.ContactCooldown;
        var contactPoint = inRiskZone ? (nearestZone.Zone!.Lat, nearestZone.Zone.Lon) : leadPosition;
        var zoneName = nearestZone.Zone?.Name ?? "open route";
        var (contactEvent, hostiles, saluteText) =
            ResolveAmbush(o, reporter, now, nowTs, contactPoint, CourseBetween(from, to), zoneName);

        updates.Add(contactEvent);
        updates.AddRange(hostiles);
        _latestSalute = saluteText;

        var friendlyCasualtiesThisContact = _casualtiesByVehicleIndex.Values.Sum();
        if (friendlyCasualtiesThisContact > 0) updates.Add(CasevacTask(reporter, now, nowTs, contactPoint));

        return contactEvent.ActionEvent;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UpdateBlueForce>> ProduceBlueForcesAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);
        var vehicleCount = o.SecurityVehicleCount + o.CargoVehicleCount;

        var updates = new List<UpdateBlueForce>(vehicleCount);
        lock (_stateGate)
        {
            // Read-only: advancing the leg is ProduceAsync's job, so the position
            // reported here is the serial's as of the last situation cycle. Half a
            // cycle of lag is exactly what a real BFT feed looks like anyway.
            var (from, to) = LegEndpoints(o);
            var fraction = Math.Clamp((now - _legStartTime).TotalSeconds / o.TransitDuration.TotalSeconds, 0, 1);
            var totalDistanceM = GeoMath.DistanceMeters(from.Lat, from.Lon, to.Lat, to.Lon);
            var spacingFraction = totalDistanceM > 0 ? 40.0 / totalDistanceM : 0;
            var course = CourseBetween(from, to);

            for (var i = 0; i < vehicleCount; i++)
            {
                var position = Lerp(from, to, Math.Clamp(fraction - i * spacingFraction, 0, 1));
                var casualties = _casualtiesByVehicleIndex.GetValueOrDefault(i);
                updates.Add(ConvoyBlueForce(o, i, vehicleCount, position, course, casualties, nowTs));
            }
        }

        logger.ConvoyBlueForcesProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateBlueForce>>(updates);
    }

    /// <summary>Which way the serial is currently driving. Callers hold <see cref="_stateGate" />.</summary>
    private ((double Lat, double Lon) From, (double Lat, double Lon) To) LegEndpoints(ConvoyEscortOptions o)
    {
        return _headingToEnd
            ? ((o.StartLatitude, o.StartLongitude), (o.EndLatitude, o.EndLongitude))
            : ((o.EndLatitude, o.EndLongitude), (o.StartLatitude, o.StartLongitude));
    }

    /// <summary>Advances the current leg's progress fraction [0,1]; flips direction and resets casualties on arrival.</summary>
    private double AdvanceLeg(ConvoyEscortOptions o, DateTimeOffset now)
    {
        var fraction = (now - _legStartTime).TotalSeconds / o.TransitDuration.TotalSeconds;
        if (fraction < 1.0) return fraction;

        // Arrived: turn the convoy around. A new serial gets fresh replacement personnel, which
        // is why casualties don't accumulate indefinitely across legs.
        _headingToEnd = !_headingToEnd;
        _legStartTime = now;
        _casualtiesByVehicleIndex.Clear();
        return 0;
    }

    private static (double Lat, double Lon) Lerp((double Lat, double Lon) from, (double Lat, double Lon) to, double fraction)
    {
        return (from.Lat + (to.Lat - from.Lat) * fraction, from.Lon + (to.Lon - from.Lon) * fraction);
    }

    private static double CourseBetween((double Lat, double Lon) from, (double Lat, double Lon) to)
    {
        return (Math.Atan2(to.Lon - from.Lon, to.Lat - from.Lat) * 180.0 / Math.PI + 360.0) % 360.0;
    }

    private static RiskZone[] BuildRiskZones(ConvoyEscortOptions o)
    {
        (string Name, string Description, double Fraction)[] plan =
        [
            ("Canal culvert", "Narrow culvert crossing - restricted maneuver, prior IED finds", 0.3),
            ("Market chokepoint", "Congested market street, low speed, dense crowd cover", 0.55),
            ("RR underpass", "Rail underpass, poor sightlines, single lane", 0.8)
        ];

        var start = (o.StartLatitude, o.StartLongitude);
        var end = (o.EndLatitude, o.EndLongitude);
        return plan.Select(p =>
        {
            var (lat, lon) = Lerp(start, end, p.Fraction);
            return new RiskZone(p.Name, p.Description, lat, lon);
        }).ToArray();
    }

    private static UpdateSituationObject SupplyRoute(Identity reporter, Timestamp nowTs, ConvoyEscortOptions o)
    {
        var route = new RouteLocation { LocationTime = nowTs, Name = RouteName };
        route.WayPoints.Add(new WayPoint
        {
            LatitudeCoordinate = o.StartLatitude,
            LongitudeCoordinate = o.StartLongitude,
            WayPointName = "SP",
            Comment = "Convoy start point"
        });
        foreach (var zone in BuildRiskZones(o))
            route.WayPoints.Add(new WayPoint
            {
                LatitudeCoordinate = zone.Lat,
                LongitudeCoordinate = zone.Lon,
                WayPointName = zone.Name,
                Comment = zone.Description
            });
        route.WayPoints.Add(new WayPoint
        {
            LatitudeCoordinate = o.EndLatitude,
            LongitudeCoordinate = o.EndLongitude,
            WayPointName = "RP",
            Comment = "Convoy release point"
        });

        return new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "convoy:route:condor" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = RouteName },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Main supply route, recurring logistics serial" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { RouteLocation = route } },
                RouteType = new UpdatePropertyRouteType { Content = RouteType.MainSupplyRoute },
                LineColor = new UpdatePropertyColor
                { Content = new Color { Red = 0, Green = 180, Blue = 120, Alpha = 255 } },
                LineWidth = new UpdatePropertyInt { Content = 2 }
            }
        };
    }

    /// <summary>
    ///     One vehicle of the serial, as the blue force it is. There is no free-text
    ///     field on <c>BlueForce</c> to hang "3 personnel aboard" from, so the state
    ///     that actually matters to a watcher - this truck has taken casualties, this
    ///     one is combat ineffective - goes in the callsign, which is exactly what the
    ///     contract says a callsign is for ("how the blue force should be represented
    ///     textual"). The full casualty accounting still goes up in the SALUTE report.
    /// </summary>
    private static UpdateBlueForce ConvoyBlueForce(
        ConvoyEscortOptions o, int index, int vehicleCount,
        (double Lat, double Lon) position, double course, int casualties, Timestamp nowTs)
    {
        var isLeadGunTruck = index == 0;
        var isRearGunTruck = index == vehicleCount - 1;
        var role = (isLeadGunTruck, isRearGunTruck) switch
        {
            (true, _) => "GUNTRUCK 1 (lead)",
            (_, true) => "GUNTRUCK 2 (trail)",
            _ => $"LOGPAC {index}"
        };

        var personnel = Math.Max(0, o.PersonnelPerVehicle - casualties);
        var status = string.Empty;
        if (personnel <= 0) status = " [COMBAT INEFFECTIVE]";
        else if (casualties > 0) status = $" [{casualties} WIA]";

        // Illustrative MIL-STD-2525C codes, friend affiliation: combat instillation for the
        // armed escort, sustainment for the cargo trucks - same scheme as the base scenario's
        // patrol vehicle, just swapped function-id characters.
        var sidc = isLeadGunTruck || isRearGunTruck ? "SFGPUCI--------" : "SFGPUST--------";

        return new UpdateBlueForce
        {
            Identity = new Identity { StringIdentity = $"convoy:vehicle:{index}" },

            // The call is the keep-alive; this is the timestamp the timeout runs off.
            LastContactTime = nowTs,
            Callsign = $"{ConvoyCallsign} {role}{status}",
            Symbol = new SymbolIdentifier { SymbolCatalog = SymbolCatalog.Mil2525C, StringIdentifier = sidc },
            BlueForceType = new BlueForceType { IsVehicle = true, IsLeader = isLeadGunTruck },
            PointLocation = new Point
            {
                LocationTime = nowTs,
                GeoPoint = new GeoPoint
                {
                    LatitudeCoordinate = position.Lat,
                    LongitudeCoordinate = position.Lon,
                    MeasurementCode = MeasurementCode.Gps
                },
                Course = course,

                // A disabled truck is stopped, which is half of what says it is in trouble.
                Speed = personnel <= 0 ? 0 : 8.9
            }
        };
    }

    /// <summary>
    ///     Identity of the blue force a Host's <c>Simulator:BlueForce:OwnIdentity</c>
    ///     should point at for this scenario to show an own force - the lead gun truck,
    ///     where the convoy commander rides.
    /// </summary>
    public static string OwnBlueForceIdentity => "convoy:vehicle:0";

    /// <summary>
    ///     Spawns the ambush element and resolves the engagement, returning the ActionEvent, the
    ///     hostile symbols, and a SALUTE-format report line for the persistent contact report.
    /// </summary>
    private (UpdateSituationObject ContactEvent, IReadOnlyList<UpdateSituationObject> Hostiles, string Salute)
        ResolveAmbush(
            ConvoyEscortOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs,
            (double Lat, double Lon) contactPoint, double course, string zoneName)
    {
        var isIed = _random.NextDouble() < o.IedProbabilityGivenContact;
        var hostileSize = 3 + _random.Next(6); // a small ambush team/cell, 3-8 fighters

        // Two vehicles are typically caught in the kill zone; the IED's initial blast gives the
        // ambusher a surprise edge, pure small-arms contact favors the escort's training/firepower.
        _casualtiesByVehicleIndex.TryAdd(0, 0);
        _casualtiesByVehicleIndex.TryAdd(1, 0);
        var friendlyStrength = o.PersonnelPerVehicle * 2 -
                                _casualtiesByVehicleIndex[0] - _casualtiesByVehicleIndex[1];
        var friendlyEffectiveness = isIed ? 0.18 : 0.32;
        var hostileEffectiveness = isIed ? 0.24 : 0.13;

        var outcome = LanchesterModel.Resolve(
            Math.Max(0, friendlyStrength), hostileSize, friendlyEffectiveness, hostileEffectiveness);

        // Casualties land on whichever of the two lead vehicles is unlucky, weighted by headcount.
        for (var i = 0; i < outcome.FriendlyCasualties; i++)
        {
            var vehicle = _random.Next(2);
            _casualtiesByVehicleIndex[vehicle] = _casualtiesByVehicleIndex[vehicle] + 1;
        }

        var id = $"convoy:event:{Interlocked.Increment(ref _eventCounter):D5}";
        var threatLevel = Math.Clamp(1 + outcome.FriendlyCasualties, 1, 5);
        var name = isIed ? "IED strike / ambush" : "Small-arms ambush";
        var description = $"{ConvoyCallsign} in contact IVO {zoneName}. " +
                           (isIed ? "IED detonation followed by small-arms fire. " : "Direct small-arms contact. ") +
                           $"Est. {outcome.HostileCasualties}/{hostileSize} hostile casualties, " +
                           $"{outcome.FriendlyCasualties} friendly WIA.";

        var location = isIed
            ? new SymbolLocation
            {
                Ellipse = new Ellipse
                {
                    LocationTime = nowTs,
                    Name = "Blast area",
                    CenterPoint = new GeoPoint { LatitudeCoordinate = contactPoint.Lat, LongitudeCoordinate = contactPoint.Lon },
                    FirstConjugateDiameterPoint = ToGeoPoint(GeoMath.Destination(contactPoint.Lat, contactPoint.Lon, course, 40)),
                    SecondConjugateDiameterPoint = ToGeoPoint(GeoMath.Destination(contactPoint.Lat, contactPoint.Lon, course + 90, 20))
                }
            }
            : new SymbolLocation
            {
                Fan = new Fan
                {
                    LocationTime = nowTs,
                    Name = "Engagement arc",
                    VertexPoint = new GeoPoint { LatitudeCoordinate = contactPoint.Lat, LongitudeCoordinate = contactPoint.Lon },
                    OrientationAngle = (course + 200) % 360,
                    SectorSizeAngle = 40,
                    MinimumRangeDimension = 20,
                    MaximumRangeDimension = 150
                }
            };

        var contactEvent = new UpdateSituationObject
        {
            ActionEvent = new UpdateActionEvent
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(10)) },
                Name = new UpdatePropertyString { Content = name },
                ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.Ambush },
                ThreatLevel = new UpdatePropertyInt { Content = threatLevel },
                DetectionDescription = new UpdatePropertyString { Content = description },
                Location = new UpdatePropertyLocation { Content = location }
            }
        };

        var hostiles = new List<UpdateSituationObject>();
        for (var i = 0; i < outcome.HostileRemaining; i++)
        {
            var offset = GeoMath.Destination(contactPoint.Lat, contactPoint.Lon, (course + 180 + i * 15) % 360, 60 + i * 10);
            var track = new TrackReport(
                $"convoy:hostile:{id}:{i}", $"HOSTILE {i + 1}", offset.Lat, offset.Lon,
                null, null, 0, "Ambush element, dismounted");
            hostiles.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, o.ReporterId, "SHGPUCI--------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(4)));
        }

        var salute = $"S: {hostileSize} pax dismounted / A: {name} / L: IVO {zoneName} / " +
                     $"U: unknown / T: {now:HH:mm}Z / E: small arms{(isIed ? ", IED" : "")}";

        return (contactEvent, hostiles, salute);
    }

    private static GeoPoint ToGeoPoint((double Lat, double Lon) point)
    {
        return new GeoPoint { LatitudeCoordinate = point.Lat, LongitudeCoordinate = point.Lon };
    }

    private static UpdateSituationObject CasevacTask(
        Identity reporter, DateTimeOffset now, Timestamp nowTs, (double Lat, double Lon) at)
    {
        return new UpdateSituationObject
        {
            ActionTask = new UpdateActionTask
            {
                Identity = new Identity { StringIdentity = "convoy:task:casevac" },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(20)) },
                Name = new UpdatePropertyString { Content = "CASEVAC request" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Casualty evacuation requested following contact; QRF/medevac inbound" },
                ActionTaskType = new UpdatePropertyActionTask { Content = ActionTaskType.Engage },
                ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.InProgress },
                ActionTaskPriority = new UpdatePropertyActionTaskPriorityCode { Content = ActionTaskPriorityType.Priority1 },
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point { LocationTime = nowTs, GeoPoint = ToGeoPoint(at) }
                    }
                }
            }
        };
    }

    private UpdateSituationObject SaluteReport(Identity reporter, Timestamp nowTs, DateTimeOffset now)
    {
        return new UpdateSituationObject
        {
            NatoMessageDocument = new UpdateNatoMessageDocument
            {
                Identity = new Identity { StringIdentity = "convoy:nato:salute" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = $"SALUTE report - {ConvoyCallsign}" },
                MtfMessageData = new UpdatePropertyString
                {
                    Content = "MSGID/SALUTE/TRIREME//\n" +
                              $"DTG/{now:ddHHmm}Z{now:MMMyy}//\n" +
                              $"REPORT/{_latestSalute}//"
                },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence
                {
                    Content = _latestSalute.StartsWith("No significant", StringComparison.Ordinal)
                        ? MessagePrecedenceType.Routine
                        : MessagePrecedenceType.Flash
                }
            }
        };
    }
}
