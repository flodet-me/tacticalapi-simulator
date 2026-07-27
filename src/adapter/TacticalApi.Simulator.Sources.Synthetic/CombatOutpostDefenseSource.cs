using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline scenario: a static combat outpost ("COP RESOLUTE") defended by a platoon-strength
///     garrison, probed by a persistent local hostile cell. Two things make this "real simulation"
///     rather than flavor text:
///     - Contact probability follows an actual day/night cycle (checked against wall-clock UTC
///     hour) rather than a flat rate - irregular/insurgent activity skews heavily toward
///     darkness in real COIN data, and this source reproduces that skew directly.
///     - Both sides carry persistent strength pools that deplete with losses and slowly
///     reconstitute over time (reinforcement/replacements), rather than respawning at full
///     strength every contact. Indirect-fire impacts are checked against the actual perimeter
///     polygon (<see cref="GeoMath.Contains" />) to decide whether they land inside or outside
///     the wire, and every engagement is resolved with <see cref="LanchesterModel" />.
/// </summary>
public sealed class CombatOutpostDefenseSource(
    IOptionsMonitor<CombatOutpostDefenseOptions> options,
    TimeProvider timeProvider,
    ILogger<CombatOutpostDefenseSource> logger)
    : ISimulationSource
{
    private const string CopName = "COP RESOLUTE";

    private readonly Random _random = new(options.CurrentValue.Seed);
    private readonly (double Lat, double Lon)[] _perimeter = BuildPerimeter(options.CurrentValue);
    private readonly (double Lat, double Lon)[] _observationPosts = BuildObservationPosts(options.CurrentValue);

    private double _hostileCellStrength = options.CurrentValue.InitialHostileCellStrength;
    private double _garrisonEffective = options.CurrentValue.GarrisonStrength;
    private DateTimeOffset _lastRegenTime = timeProvider.GetUtcNow();
    private DateTimeOffset? _contactCooldownUntil;
    private int _eventCounter;
    private string _latestSitrep = "No significant activity.";

    /// <inheritdoc/>
    public string Name => SimulationSourceName.FromSectionName(CombatOutpostDefenseOptions.SectionName);

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <inheritdoc/>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);
        var reporter = new Identity { StringIdentity = o.ReporterId };

        Regenerate(o, now);
        var isNight = IsNight(now, o.NightStartHourUtc, o.NightEndHourUtc);

        var updates = new List<UpdateSituationObject>
        {
            Perimeter(reporter, nowTs, o),
            DefendTask(reporter, nowTs, isNight)
        };
        for (var i = 0; i < _observationPosts.Length; i++)
            updates.Add(ObservationPost(o, i));

        var contactProbability = isNight ? o.DayContactProbability * o.NightContactProbabilityMultiplier : o.DayContactProbability;
        var cooldownElapsed = _contactCooldownUntil is null || now >= _contactCooldownUntil;

        if (cooldownElapsed && _hostileCellStrength >= 1 && _random.NextDouble() < contactProbability)
        {
            _contactCooldownUntil = now + o.ContactCooldown;
            var contactRoll = _random.NextDouble();
            var (contactUpdates, sitrep) = contactRoll switch
            {
                _ when contactRoll < o.AssaultProbabilityGivenContact => ResolveAssault(o, reporter, now, nowTs),
                _ when contactRoll < o.AssaultProbabilityGivenContact + o.IndirectFireProbabilityGivenContact =>
                    ResolveIndirectFire(o, reporter, now, nowTs),
                _ => ResolveProbe(o, reporter, now, nowTs)
            };
            updates.AddRange(contactUpdates);
            _latestSitrep = sitrep;
        }

        updates.Add(SitrepMessage(reporter, nowTs, now, isNight));

        logger.ScenarioCycleProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    // ---------------------------------------------------------------- time/strength bookkeeping

    private void Regenerate(CombatOutpostDefenseOptions o, DateTimeOffset now)
    {
        var hoursElapsed = (now - _lastRegenTime).TotalHours;
        _lastRegenTime = now;
        if (hoursElapsed <= 0) return;

        _hostileCellStrength = Math.Min(o.InitialHostileCellStrength,
            _hostileCellStrength + o.HostileReinforcementPerHour * hoursElapsed);
        _garrisonEffective = Math.Min(o.GarrisonStrength,
            _garrisonEffective + o.GarrisonReplacementPerHour * hoursElapsed);
    }

    /// <summary>Real insurgent/irregular activity skews heavily toward darkness; this is that skew, checked against the actual clock.</summary>
    private static bool IsNight(DateTimeOffset now, int startHour, int endHour)
    {
        var hour = now.Hour;
        return startHour <= endHour ? hour >= startHour && hour < endHour : hour >= startHour || hour < endHour;
    }

    // ---------------------------------------------------------------- static perimeter/OPs

    private static (double Lat, double Lon)[] BuildPerimeter(CombatOutpostDefenseOptions o)
    {
        // S2245: seeded on purpose for a reproducible perimeter layout given the
        // same Seed - not a security context, so a cryptographic RNG (which can't
        // be seeded this way) would defeat the point.
#pragma warning disable S2245
        var jitter = new Random(o.Seed);
#pragma warning restore S2245
        const int points = 8;
        var perimeter = new (double Lat, double Lon)[points];
        for (var i = 0; i < points; i++)
        {
            var bearing = i * (360.0 / points);
            var radius = o.PerimeterRadiusM * (0.9 + jitter.NextDouble() * 0.2);
            perimeter[i] = GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, bearing, radius);
        }

        return perimeter;
    }

    private static (double Lat, double Lon)[] BuildObservationPosts(CombatOutpostDefenseOptions o)
    {
        var posts = new (double Lat, double Lon)[o.ObservationPostCount];
        for (var i = 0; i < posts.Length; i++)
        {
            var bearing = i * (360.0 / posts.Length);
            posts[i] = GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, bearing, o.PerimeterRadiusM * 0.95);
        }

        return posts;
    }

    private static UpdateSituationObject Perimeter(Identity reporter, Timestamp nowTs, CombatOutpostDefenseOptions o)
    {
        var polygon = new Polygon { LocationTime = nowTs, Name = $"{CopName} perimeter" };
        foreach (var (lat, lon) in BuildPerimeter(o))
            polygon.Points.Add(new GeoPoint { LatitudeCoordinate = lat, LongitudeCoordinate = lon });

        return new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "cop:perimeter" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = $"{CopName} perimeter" },
                AdditionalInformation = new UpdatePropertyString { Content = "Defended perimeter, platoon-strength COP" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Polygon = polygon } }
            }
        };
    }

    private UpdateSituationObject ObservationPost(CombatOutpostDefenseOptions o, int index)
    {
        var (lat, lon) = _observationPosts[index];
        var track = new TrackReport($"cop:op:{index}", $"OP {index + 1}", lat, lon, null, null, 0, "Manned observation post");
        return TrackUpdateFactory.CreateSymbolUpdate(
            track, o.ReporterId, "SFGPUCI--------", SymbolCatalog.Mil2525C, timeProvider.GetUtcNow(), TimeSpan.FromMinutes(2));
    }

    private UpdateSituationObject DefendTask(Identity reporter, Timestamp nowTs, bool isNight)
    {
        var o = options.CurrentValue;
        return new UpdateSituationObject
        {
            ActionTask = new UpdateActionTask
            {
                Identity = new Identity { StringIdentity = "cop:task:defend" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = $"Defend {CopName}" },
                AdditionalInformation = new UpdatePropertyString
                {
                    Content = $"Garrison {Math.Round(_garrisonEffective)}/{options.CurrentValue.GarrisonStrength} effective. " +
                              $"Estimated hostile cell strength {Math.Round(_hostileCellStrength)}. " +
                              (isNight ? "Stand-to: elevated alert (night)." : "Routine daylight posture.")
                },
                ActionTaskType = new UpdatePropertyActionTask { Content = ActionTaskType.Engage },
                ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.InProgress },
                ActionTaskPriority = new UpdatePropertyActionTaskPriorityCode
                { Content = isNight ? ActionTaskPriorityType.Priority1 : ActionTaskPriorityType.Priority3 },
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point
                        {
                            LocationTime = nowTs,
                            GeoPoint = new GeoPoint { LatitudeCoordinate = o.CenterLatitude, LongitudeCoordinate = o.CenterLongitude }
                        }
                    }
                }
            }
        };
    }

    // ---------------------------------------------------------------- contact resolution

    private (IReadOnlyList<UpdateSituationObject> Updates, string Sitrep) ResolveIndirectFire(
        CombatOutpostDefenseOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs)
    {
        var bearing = _random.NextDouble() * 360;
        var distance = _random.NextDouble() * o.PerimeterRadiusM * 1.4; // mortar dispersion can miss the perimeter entirely
        var (impactLat, impactLon) = GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, bearing, distance);
        var insideWire = GeoMath.Contains(_perimeter, impactLat, impactLon);

        var casualties = 0;
        if (insideWire)
            for (var i = 0; i < 6; i++) // a handful of exposed personnel could plausibly be caught in the open
                if (_random.NextDouble() < 0.12)
                    casualties++;
        casualties = Math.Min(casualties, (int)_garrisonEffective);
        _garrisonEffective -= casualties;

        var id = $"cop:event:{Interlocked.Increment(ref _eventCounter):D5}";
        var casualtySuffix = casualties == 1 ? "y" : "ies";
        var description = insideWire
            ? $"Indirect fire impact inside the wire. {casualties} friendly casualt{casualtySuffix}."
            : "Indirect fire impact outside the wire, no casualties.";

        var major = GeoMath.Destination(impactLat, impactLon, bearing, 25);
        var minor = GeoMath.Destination(impactLat, impactLon, bearing + 90, 15);
        var updates = new List<UpdateSituationObject>
        {
            new()
            {
                ActionEvent = new UpdateActionEvent
                {
                    Identity = new Identity { StringIdentity = id },
                    Reporter = reporter,
                    ReportingTime = nowTs,
                    ExpiryTime = new UpdatePropertyTimestamp
                    { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(15)) },
                    Name = new UpdatePropertyString { Content = "Indirect fire" },
                    ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.ArtilleryFire },
                    ThreatLevel = new UpdatePropertyInt { Content = insideWire ? Math.Clamp(2 + casualties, 2, 5) : 1 },
                    DetectionDescription = new UpdatePropertyString { Content = description },
                    Location = new UpdatePropertyLocation
                    {
                        Content = new SymbolLocation
                        {
                            Ellipse = new Ellipse
                            {
                                LocationTime = nowTs,
                                Name = "Impact area",
                                CenterPoint = new GeoPoint { LatitudeCoordinate = impactLat, LongitudeCoordinate = impactLon },
                                FirstConjugateDiameterPoint =
                                    new GeoPoint { LatitudeCoordinate = major.Lat, LongitudeCoordinate = major.Lon },
                                SecondConjugateDiameterPoint =
                                    new GeoPoint { LatitudeCoordinate = minor.Lat, LongitudeCoordinate = minor.Lon }
                            }
                        }
                    }
                }
            }
        };

        logger.IncidentRaised(id, "Indirect fire", casualties);
        var salute = $"S: unk mortar tube / A: indirect fire / L: {(insideWire ? "inside" : "outside")} the wire / " +
                     $"U: {CopName} / T: {now:HH:mm}Z / E: mortar";
        return (updates, salute);
    }

    private (IReadOnlyList<UpdateSituationObject> Updates, string Sitrep) ResolveProbe(
        CombatOutpostDefenseOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs)
    {
        var standoffBearing = _random.NextDouble() * 360;
        var standoffDistance = 150 + _random.NextDouble() * 250;
        var (standLat, standLon) = GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, standoffBearing, standoffDistance);
        var inwardBearing = (standoffBearing + 180) % 360;

        var hostileCommitted = Math.Min(_hostileCellStrength, 1 + _random.Next(3));
        const int friendlyResponders = 6; // the nearest OP's element
        var outcome = LanchesterModel.Resolve(friendlyResponders, (int)Math.Round(hostileCommitted),
            friendlyEffectiveness: 0.30, hostileEffectiveness: 0.10, ticks: 5);

        _hostileCellStrength = Math.Max(0, _hostileCellStrength - outcome.HostileCasualties);
        _garrisonEffective = Math.Max(0, _garrisonEffective - outcome.FriendlyCasualties);

        var id = $"cop:event:{Interlocked.Increment(ref _eventCounter):D5}";
        var description = $"Harassing fire from stand-off, probing perimeter defenses. " +
                           $"Est. {outcome.HostileCasualties}/{Math.Round(hostileCommitted)} hostile casualties, " +
                           $"{outcome.FriendlyCasualties} friendly WIA.";

        var updates = new List<UpdateSituationObject>
        {
            new()
            {
                ActionEvent = new UpdateActionEvent
                {
                    Identity = new Identity { StringIdentity = id },
                    Reporter = reporter,
                    ReportingTime = nowTs,
                    ExpiryTime = new UpdatePropertyTimestamp
                    { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(10)) },
                    Name = new UpdatePropertyString { Content = "Sniper/harassing fire" },
                    ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.SniperAttack },
                    ThreatLevel = new UpdatePropertyInt { Content = Math.Clamp(1 + outcome.FriendlyCasualties, 1, 5) },
                    DetectionDescription = new UpdatePropertyString { Content = description },
                    Location = new UpdatePropertyLocation
                    {
                        Content = new SymbolLocation
                        {
                            Fan = new Fan
                            {
                                LocationTime = nowTs,
                                Name = "Harassing fire arc",
                                VertexPoint = new GeoPoint { LatitudeCoordinate = standLat, LongitudeCoordinate = standLon },
                                OrientationAngle = (inwardBearing - 15 + 360) % 360,
                                SectorSizeAngle = 30,
                                MinimumRangeDimension = 10,
                                MaximumRangeDimension = standoffDistance
                            }
                        }
                    }
                }
            }
        };

        for (var i = 0; i < outcome.HostileRemaining; i++)
        {
            var offset = GeoMath.Destination(standLat, standLon, standoffBearing + i * 20, 15 + i * 10);
            var track = new TrackReport($"cop:hostile:{id}:{i}", $"HOSTILE {i + 1}", offset.Lat, offset.Lon,
                null, null, 0, "Probing element");
            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, o.ReporterId, "SHGPUCI--------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(3)));
        }

        logger.IncidentRaised(id, "Sniper/harassing fire", outcome.FriendlyCasualties);
        var salute = $"S: ~{Math.Round(hostileCommitted)} pax / A: harassing/probing fire / L: stand-off {Math.Round(standoffDistance)}m / " +
                     $"U: {CopName} / T: {now:HH:mm}Z / E: small arms";
        return (updates, salute);
    }

    private (IReadOnlyList<UpdateSituationObject> Updates, string Sitrep) ResolveAssault(
        CombatOutpostDefenseOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs)
    {
        var bearing = _random.NextDouble() * 360;
        var standoffDistance = 120 + _random.NextDouble() * 100;
        var (standLat, standLon) = GeoMath.Destination(o.CenterLatitude, o.CenterLongitude, bearing, standoffDistance);
        var inwardBearing = (bearing + 180) % 360;

        var hostileCommitted = Math.Max(1, _hostileCellStrength * (0.4 + _random.NextDouble() * 0.3));
        var friendlyDefenders = Math.Max(1, (int)Math.Round(_garrisonEffective * 0.5)); // half the garrison stands-to

        var outcome = LanchesterModel.Resolve(friendlyDefenders, (int)Math.Round(hostileCommitted),
            friendlyEffectiveness: 0.25, hostileEffectiveness: 0.20, ticks: 9);

        _hostileCellStrength = Math.Max(0, _hostileCellStrength - outcome.HostileCasualties);
        _garrisonEffective = Math.Max(0, _garrisonEffective - outcome.FriendlyCasualties);

        var id = $"cop:event:{Interlocked.Increment(ref _eventCounter):D5}";
        var description = $"Coordinated ground assault on {CopName}, direction {Math.Round(inwardBearing)}°T. " +
                           $"Est. {outcome.HostileCasualties}/{Math.Round(hostileCommitted)} hostile casualties, " +
                           $"{outcome.FriendlyCasualties} friendly WIA/KIA.";

        var updates = new List<UpdateSituationObject>
        {
            new()
            {
                ActionEvent = new UpdateActionEvent
                {
                    Identity = new Identity { StringIdentity = id },
                    Reporter = reporter,
                    ReportingTime = nowTs,
                    ExpiryTime = new UpdatePropertyTimestamp
                    { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(20)) },
                    Name = new UpdatePropertyString { Content = "Ground assault" },
                    ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.Ambush },
                    ThreatLevel = new UpdatePropertyInt { Content = 5 },
                    DetectionDescription = new UpdatePropertyString { Content = description },
                    Location = new UpdatePropertyLocation
                    {
                        Content = new SymbolLocation
                        {
                            Fan = new Fan
                            {
                                LocationTime = nowTs,
                                Name = "Assault axis",
                                VertexPoint = new GeoPoint { LatitudeCoordinate = standLat, LongitudeCoordinate = standLon },
                                OrientationAngle = (inwardBearing - 25 + 360) % 360,
                                SectorSizeAngle = 50,
                                MinimumRangeDimension = 10,
                                MaximumRangeDimension = standoffDistance
                            }
                        }
                    }
                }
            }
        };

        for (var i = 0; i < outcome.HostileRemaining; i++)
        {
            var offset = GeoMath.Destination(standLat, standLon, bearing + i * 12, 20 + i * 15);
            var track = new TrackReport($"cop:hostile:{id}:{i}", $"HOSTILE {i + 1}", offset.Lat, offset.Lon,
                null, inwardBearing, 1.0, "Assault element, advancing");
            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, o.ReporterId, "SHGPUCI--------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(5)));
        }

        if (outcome.FriendlyCasualties >= 3)
            updates.Add(new UpdateSituationObject
            {
                ActionTask = new UpdateActionTask
                {
                    Identity = new Identity { StringIdentity = "cop:task:qrf" },
                    Reporter = reporter,
                    ReportingTime = nowTs,
                    ExpiryTime = new UpdatePropertyTimestamp
                    { Content = Timestamp.FromDateTimeOffset(now + TimeSpan.FromMinutes(30)) },
                    Name = new UpdatePropertyString { Content = "QRF reinforcement" },
                    AdditionalInformation = new UpdatePropertyString
                    { Content = $"Quick reaction force requested - {CopName} under sustained assault" },
                    ActionTaskType = new UpdatePropertyActionTask { Content = ActionTaskType.Engage },
                    ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.Tasked },
                    ActionTaskPriority = new UpdatePropertyActionTaskPriorityCode { Content = ActionTaskPriorityType.Priority1 },
                    Location = new UpdatePropertyLocation
                    {
                        Content = new SymbolLocation
                        {
                            Point = new Point
                            {
                                LocationTime = nowTs,
                                GeoPoint = new GeoPoint { LatitudeCoordinate = o.CenterLatitude, LongitudeCoordinate = o.CenterLongitude }
                            }
                        }
                    }
                }
            });

        logger.IncidentRaised(id, "Ground assault", outcome.FriendlyCasualties);
        var salute = $"S: ~{Math.Round(hostileCommitted)} pax / A: ground assault / L: axis {Math.Round(inwardBearing)}°T / " +
                     $"U: {CopName} / T: {now:HH:mm}Z / E: small arms, RPG";
        return (updates, salute);
    }

    private UpdateSituationObject SitrepMessage(Identity reporter, Timestamp nowTs, DateTimeOffset now, bool isNight)
    {
        return new UpdateSituationObject
        {
            NatoMessageDocument = new UpdateNatoMessageDocument
            {
                Identity = new Identity { StringIdentity = "cop:nato:sitrep" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = $"SITREP - {CopName}" },
                MtfMessageData = new UpdatePropertyString
                {
                    Content = "MSGID/SITREP/RESOLUTE//\n" +
                              $"DTG/{now:ddHHmm}Z{now:MMMyy}//\n" +
                              $"POSTURE/{(isNight ? "STAND-TO" : "ROUTINE")}//\n" +
                              $"REPORT/{_latestSitrep}//"
                },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence
                {
                    Content = _latestSitrep.StartsWith("No significant", StringComparison.Ordinal)
                        ? MessagePrecedenceType.Routine
                        : MessagePrecedenceType.Flash
                }
            }
        };
    }
}
