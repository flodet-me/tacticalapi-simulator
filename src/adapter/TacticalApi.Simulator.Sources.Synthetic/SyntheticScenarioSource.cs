using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline source that exercises ALL 11 situation object types - and most of the TacticalAPI
///     v0 location kinds - in one coherent mini scenario:
///     - OrganizationUnit: a company HQ with two subordinated platoons (ORBAT)
///     - Route BRAVO:      an irregular patrol loop with six named/commented waypoints (RouteLocation)
///     - Route CHARLIE:    a resupply air corridor from the rear area to the objective (Corridor)
///     - Symbol:           a patrol vehicle moving along Route BRAVO, plus an objective area (Polygon)
///     - ActionTask:       the patrol order, progressing NOT_STARTED -> IN_PROGRESS -> COMPLETE
///     with a live completion ratio
///     - ActionEvent:      random incidents near the route - a sensor detection arc (Fan), an
///     artillery impact area (Ellipse), a discovered device cluster (Multipoint), or a plain
///     point incident - expiring via TTL
///     - TextDocument:     periodic SITREP chat messages
///     - NatoMessageDocument: an OWNSITREP MTF message
///     - PictureDocument:  recon photo (tiny embedded PNG)
///     - VoiceMessageDocument: radio check (tiny embedded WAV)
///     - SketchDocument:   a multi-element planning sketch (objective outline + axis of advance +
///     rally point), each element individually styled
///     - OverlayDocument:  an overlay carrying two nested phase-line symbols
///     Everything is emitted as plain UpdateSituationObject batches through the standard ingest
///     path - exactly like an external TacticalAPI client.
/// </summary>
public sealed class SyntheticScenarioSource(
    IOptionsMonitor<SyntheticScenarioOptions> options,
    TimeProvider timeProvider,
    ILogger<SyntheticScenarioSource> logger)
    : ISimulationSource
{
    private const double EarthRadiusKm = 6371.0;

    // 1x1 transparent PNG - a minimal valid picture payload.
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    // Minimal valid WAV: 44-byte PCM header, 8 kHz mono 8-bit, zero samples.
    private static readonly byte[] TinyWav = CreateTinyWav();
    private readonly DateTimeOffset _epoch = timeProvider.GetUtcNow();

    // Static scenario geography, built once from the seed so the shapes stay stable across
    // cycles (unlike _random below, which drives per-cycle incident selection).
    private readonly ScenarioGeometry _geometry = BuildGeometry(options.CurrentValue);

    private readonly Random _random = new(options.CurrentValue.Seed);
    private int _chatCounter;
    private int _eventCounter;
    private ActionTaskStatusType? _lastTaskStatus;

    /// <inheritdoc/>
    public string Name => SimulationSourceName.FromSectionName(SyntheticScenarioOptions.SectionName);

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
        var geo = _geometry;

        var updates = new List<UpdateSituationObject>
        {
            // --- ORBAT: company + two platoons -------------------------------
            OrganizationUnit(reporter, nowTs, "scenario:unit:coy", "A Coy", UnitDesignation.Company,
                "PzBtl 901", (0, 90, 200),
                ["scenario:unit:plt1", "scenario:unit:plt2"]),
            OrganizationUnit(reporter, nowTs, "scenario:unit:plt1", "1st Platoon", UnitDesignation.Platoon,
                "A Coy", (0, 120, 220), []),
            OrganizationUnit(reporter, nowTs, "scenario:unit:plt2", "2nd Platoon", UnitDesignation.Platoon,
                "A Coy", (0, 120, 220), []),

            // --- Patrol route (RouteLocation - named, commented waypoints) ----
            PatrolRoute(reporter, nowTs, o, geo),

            // --- Resupply air corridor into the objective (Corridor) ----------
            SupportCorridor(reporter, nowTs, geo),

            // --- Patrol vehicle moving along the route ------------------------
            PatrolSymbol(o, now, geo),

            // --- The patrol order with live progress --------------------------
            PatrolTask(o, reporter, now, nowTs, geo),

            // --- Objective area (Polygon) --------------------------------------
            ObjectiveArea(reporter, nowTs, geo),

            // --- Documents ----------------------------------------------------
            NatoMessage(reporter, nowTs, now),
            ReconPicture(reporter, nowTs, geo.PatrolWaypoints[1]),
            RadioCheck(reporter, nowTs),
            PlanningSketch(reporter, nowTs, geo),
            PhaseLineOverlay(reporter, nowTs, geo.PatrolWaypoints)
        };

        // --- Spontaneous incidents (expire automatically via TTL) -------------
        if (_random.NextDouble() < o.EventProbability)
        {
            var incident = RandomIncident(o, reporter, now, nowTs, geo);
            updates.Add(incident);
            logger.IncidentRaised(
                incident.ActionEvent.Identity.StringIdentity,
                incident.ActionEvent.Name.Content,
                incident.ActionEvent.ThreatLevel.Content ?? 0);
        }

        // --- SITREP chat -------------------------------------------------------
        if (_random.NextDouble() < o.ChatProbability)
        {
            var chat = SitrepChat(reporter, nowTs, now);
            updates.Add(chat);
            logger.ChatMessageSent(chat.TextDocument.Identity.StringIdentity, chat.TextDocument.PlainContent.Content);
        }

        logger.ScenarioCycleProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    // ---------------------------------------------------------------- geometry

    private sealed record RouteWaypoint(double Lat, double Lon, string Name, string Comment);

    private sealed record ScenarioGeometry(
        RouteWaypoint[] PatrolWaypoints,
        (double Lat, double Lon)[] ObjectiveArea,
        (double Lat, double Lon) ObjectiveCentroid,
        (double Lat, double Lon) ForwardOperatingBase);

    /// <summary>
    ///     Lays out the whole scenario's geography once from <see cref="SyntheticScenarioOptions.Seed" />:
    ///     an irregular six-waypoint patrol loop, a pentagon-ish objective area near one of its
    ///     legs, and a forward operating base outside the loop for the resupply corridor to
    ///     originate from. Deterministic per seed so the shapes are stable across cycles.
    /// </summary>
    private static ScenarioGeometry BuildGeometry(SyntheticScenarioOptions o)
    {
        var dLat = o.ExtentKm / 2 / EarthRadiusKm * (180.0 / Math.PI);
        var dLon = dLat / Math.Cos(o.CenterLatitude * Math.PI / 180.0);
        var jitter = new Random(o.Seed);

        (string Name, string Comment, double DLatFactor, double DLonFactor)[] plan =
        [
            ("SP", "Start point, motor pool", -1.0, -1.0),
            ("CP1", "Checkpoint at the crossroads", -1.0, 0.3),
            ("CP2", "Overwatch ridge", -0.2, 1.0),
            ("CP3", "Village outskirts", 1.0, 1.0),
            ("CP4", "River crossing", 1.0, -0.4),
            ("RP", "Release point, rally back to SP", 0.3, -1.0)
        ];

        var waypoints = plan.Select(p =>
        {
            var jitterLat = (jitter.NextDouble() - 0.5) * 0.15;
            var jitterLon = (jitter.NextDouble() - 0.5) * 0.15;
            return new RouteWaypoint(
                o.CenterLatitude + (p.DLatFactor + jitterLat) * dLat,
                o.CenterLongitude + (p.DLonFactor + jitterLon) * dLon,
                p.Name, p.Comment);
        }).ToArray();

        // Objective area: an irregular pentagon near the village outskirts checkpoint.
        var objectiveCenter = waypoints[3];
        var objectiveRadiusM = o.ExtentKm * 0.12 * 1000;
        var objectiveArea = new (double Lat, double Lon)[5];
        for (var i = 0; i < objectiveArea.Length; i++)
        {
            var bearing = i * (360.0 / objectiveArea.Length) + jitter.NextDouble() * 20 - 10;
            var radius = objectiveRadiusM * (0.7 + jitter.NextDouble() * 0.3);
            objectiveArea[i] = Destination(objectiveCenter.Lat, objectiveCenter.Lon, bearing, radius);
        }

        var objectiveCentroid = (
            objectiveArea.Average(p => p.Lat),
            objectiveArea.Average(p => p.Lon));

        // Forward operating base: outside the loop, southwest of the start point.
        var fob = Destination(waypoints[0].Lat, waypoints[0].Lon, 225, o.ExtentKm * 0.4 * 1000);

        return new ScenarioGeometry(waypoints, objectiveArea, objectiveCentroid, fob);
    }

    /// <summary>Great-circle destination point, given a start point, bearing and distance.</summary>
    private static (double Lat, double Lon) Destination(double lat, double lon, double bearingDeg, double distanceM)
    {
        return GeoMath.Destination(lat, lon, bearingDeg, distanceM);
    }

    private static GeoPoint Geo(double lat, double lon)
    {
        return new GeoPoint { LatitudeCoordinate = lat, LongitudeCoordinate = lon };
    }

    // ---------------------------------------------------------------- helpers

    private static UpdateSituationObject OrganizationUnit(
        Identity reporter, Timestamp nowTs, string id, string name, UnitDesignation designation,
        string higherFormation, (int R, int G, int B) color, string[] subordinates)
    {
        var unit = new UpdateOrganizationUnit
        {
            Identity = new Identity { StringIdentity = id },
            Reporter = reporter,
            ReportingTime = nowTs,
            Name = new UpdatePropertyString { Content = name },
            HigherFormation = new UpdatePropertyString { Content = higherFormation },
            UnitDesignation = new UpdatePropertyUnitDesignation { Content = designation },
            OrganizationUnitColor = new UpdatePropertyColor
            {
                Content = new Color { Red = color.R, Green = color.G, Blue = color.B, Alpha = 255 }
            },
            SymbolIdentifier = new UpdatePropertySymbolIdentifier
            {
                Content = new SymbolIdentifier
                {
                    SymbolCatalog = SymbolCatalog.Mil2525C,
                    StringIdentifier = "SFGPU----------"
                }
            }
        };

        var refs = new UpdatePropertyReferences();
        foreach (var sub in subordinates) refs.Contents.Add(new Identity { StringIdentity = sub });

        unit.SubordinatedOrganizationUnitCollection = refs;
        return new UpdateSituationObject { OrganizationUnit = unit };
    }

    /// <summary>Route BRAVO: the patrol loop, as a RouteLocation with named, commented waypoints and ETAs.</summary>
    private static UpdateSituationObject PatrolRoute(
        Identity reporter, Timestamp nowTs, SyntheticScenarioOptions o, ScenarioGeometry geo)
    {
        var wp = geo.PatrolWaypoints;
        var legDuration = o.PatrolLapDuration / wp.Length;
        var route = new RouteLocation { LocationTime = nowTs, Name = "Route BRAVO" };

        for (var i = 0; i <= wp.Length; i++)
        {
            var point = wp[i % wp.Length];
            route.WayPoints.Add(new WayPoint
            {
                LatitudeCoordinate = point.Lat,
                LongitudeCoordinate = point.Lon,
                WayPointName = point.Name,
                Comment = point.Comment,
                SegmentTravelTime = Duration.FromTimeSpan(legDuration),
                ArrivalTime = Timestamp.FromDateTimeOffset(nowTs.ToDateTimeOffset() + legDuration * i)
            });
        }

        return new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "scenario:route:bravo" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Route BRAVO" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Daily patrol loop, six checkpoints" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { RouteLocation = route } },
                MarchSpeed = new UpdatePropertyInt { Content = 40 },
                LineColor = new UpdatePropertyColor
                { Content = new Color { Red = 0, Green = 128, Blue = 255, Alpha = 255 } },
                LineWidth = new UpdatePropertyInt { Content = 3 },
                LineStyle = new UpdatePropertyLineStyle { Content = LineStyle.Dash },
                RouteType = new UpdatePropertyRouteType { Content = RouteType.MainSupplyRoute }
            }
        };
    }

    /// <summary>Route CHARLIE: a wide resupply air corridor from the rear area into the objective.</summary>
    private static UpdateSituationObject SupportCorridor(Identity reporter, Timestamp nowTs, ScenarioGeometry geo)
    {
        var corridor = new Corridor { LocationTime = nowTs, Name = "Route CHARLIE", Width = 500 };
        corridor.Points.Add(Geo(geo.ForwardOperatingBase.Lat, geo.ForwardOperatingBase.Lon));
        corridor.Points.Add(Geo(geo.ObjectiveCentroid.Lat, geo.ObjectiveCentroid.Lon));

        return new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "scenario:route:charlie" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Route CHARLIE" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Resupply air corridor, FOB to objective, 500 m wide" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Corridor = corridor } },
                MarchSpeed = new UpdatePropertyInt { Content = 60 },
                LineColor = new UpdatePropertyColor
                { Content = new Color { Red = 155, Green = 89, Blue = 182, Alpha = 255 } },
                LineWidth = new UpdatePropertyInt { Content = 2 },
                LineStyle = new UpdatePropertyLineStyle { Content = LineStyle.Dot },
                RouteType = new UpdatePropertyRouteType { Content = RouteType.AirCorridor }
            }
        };
    }

    /// <summary>An objective/assembly area near Route BRAVO's village checkpoint, as a Polygon.</summary>
    private static UpdateSituationObject ObjectiveArea(Identity reporter, Timestamp nowTs, ScenarioGeometry geo)
    {
        var polygon = new Polygon { LocationTime = nowTs, Name = "Objective HOTEL" };
        foreach (var (lat, lon) in geo.ObjectiveArea) polygon.Points.Add(Geo(lat, lon));

        return new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "scenario:objective:hotel" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Objective HOTEL" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Assembly area for phase 3, secure before nightfall" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Polygon = polygon } },
                ForeignKey = new UpdatePropertyIdentity
                { Content = new Identity { StringIdentity = "GIS-4471" }, Source = "gis" }
            }
        };
    }

    /// <summary>Patrol vehicle position interpolated along the route perimeter.</summary>
    private UpdateSituationObject PatrolSymbol(SyntheticScenarioOptions o, DateTimeOffset now, ScenarioGeometry geo)
    {
        var (lat, lon, course) = PositionOnPerimeter(o, now, geo.PatrolWaypoints);
        var track = new TrackReport(
            "scenario:symbol:patrol1",
            "PATROL 1 (1st Plt)",
            lat,
            lon,
            null,
            course,
            11,
            "On route BRAVO, task scenario:task:patrol");

        // Expiry generously above the update interval so the track never
        // flickers out between cycles.
        var symbolUpdate = TrackUpdateFactory.CreateSymbolUpdate(
            track, o.ReporterId, "SFGPUCI--------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(5));
        return symbolUpdate;
    }

    private (double Lat, double Lon, double Course) PositionOnPerimeter(
        SyntheticScenarioOptions o, DateTimeOffset now, RouteWaypoint[] wp)
    {
        var lapFraction = (now - _epoch).TotalSeconds / o.PatrolLapDuration.TotalSeconds % 1.0;
        var segment = (int)(lapFraction * wp.Length);
        var segmentFraction = lapFraction * wp.Length - segment;
        var from = wp[segment % wp.Length];
        var to = wp[(segment + 1) % wp.Length];

        var lat = from.Lat + (to.Lat - from.Lat) * segmentFraction;
        var lon = from.Lon + (to.Lon - from.Lon) * segmentFraction;
        var course = (Math.Atan2(to.Lon - from.Lon, to.Lat - from.Lat) * 180.0 / Math.PI + 360.0) % 360.0;
        return (lat, lon, course);
    }

    /// <summary>The patrol order; status and completion follow the current lap.</summary>
    private UpdateSituationObject PatrolTask(
        SyntheticScenarioOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs, ScenarioGeometry geo)
    {
        var waypoints = geo.PatrolWaypoints;
        var lapFraction = (now - _epoch).TotalSeconds / o.PatrolLapDuration.TotalSeconds % 1.0;
        var status = lapFraction switch
        {
            < 0.02 => ActionTaskStatusType.NotStarted,
            > 0.98 => ActionTaskStatusType.Complete,
            _ => ActionTaskStatusType.InProgress
        };

        if (_lastTaskStatus != status)
        {
            logger.PatrolTaskStatusChanged(status.ToString());
            _lastTaskStatus = status;
        }

        var task = new UpdateActionTask
        {
            Identity = new Identity { StringIdentity = "scenario:task:patrol" },
            Reporter = reporter,
            ReportingTime = nowTs,
            Name = new UpdatePropertyString { Content = "Patrol route BRAVO" },
            AdditionalInformation = new UpdatePropertyString
            { Content = "Recon patrol along route BRAVO, report all activity" },
            ActionTaskType = new UpdatePropertyActionTask { Content = ActionTaskType.Reconnaissance },
            ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = status },
            ActionTaskPriority =
                new UpdatePropertyActionTaskPriorityCode { Content = ActionTaskPriorityType.Priority2 },
            CompletionRatio = new UpdatePropertyInt { Content = (int)(lapFraction * 100) },
            PlannedStartTime = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(_epoch) },
            PlannedEndTime = new UpdatePropertyTimestamp
            { Content = Timestamp.FromDateTimeOffset(_epoch + o.PatrolLapDuration) },
            Location = new UpdatePropertyLocation
            {
                Content = new SymbolLocation
                {
                    Point = new Point
                    {
                        LocationTime = nowTs,
                        GeoPoint = Geo(waypoints[0].Lat, waypoints[0].Lon)
                    }
                }
            }
        };

        task.ActionTaskResources = new UpdatePropertyReferences();
        task.ActionTaskResources.Contents.Add(new Identity { StringIdentity = "scenario:unit:plt1" });
        task.ActionTaskResources.Contents.Add(new Identity { StringIdentity = "scenario:symbol:patrol1" });

        return new UpdateSituationObject { ActionTask = task };
    }

    private UpdateSituationObject RandomIncident(
        SyntheticScenarioOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs, ScenarioGeometry geo)
    {
        var waypoints = geo.PatrolWaypoints;
        (ActionEventType Type, string Name, int Threat)[] catalog =
        [
            (ActionEventType.SniperAttack, "Sniper attack", 4),
            (ActionEventType.ArtilleryFire, "Artillery fire observed", 5),
            (ActionEventType.BoobyTrapDiscovery, "Booby trap belt discovered", 3),
            (ActionEventType.AccidentTraffic, "Traffic accident", 1),
            (ActionEventType.FixAcoustic, "Acoustic fix", 2)
        ];
        var incident = catalog[_random.Next(catalog.Length)];
        var anchor = waypoints[_random.Next(waypoints.Length)];
        var lat = anchor.Lat + (_random.NextDouble() - 0.5) * 0.02;
        var lon = anchor.Lon + (_random.NextDouble() - 0.5) * 0.02;
        var id = $"scenario:event:{Interlocked.Increment(ref _eventCounter):D5}";

        var location = incident.Type switch
        {
            ActionEventType.SniperAttack => SensorArcLocation(nowTs, lat, lon),
            ActionEventType.ArtilleryFire => ImpactAreaLocation(nowTs, lat, lon),
            ActionEventType.BoobyTrapDiscovery => DeviceClusterLocation(nowTs, lat, lon),
            _ => new SymbolLocation
            {
                Point = new Point { LocationTime = nowTs, GeoPoint = Geo(lat, lon) }
            }
        };

        return new UpdateSituationObject
        {
            ActionEvent = new UpdateActionEvent
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now + o.EventTimeToLive) },
                Name = new UpdatePropertyString { Content = incident.Name },
                ActionEventType = new UpdatePropertyActionEvent { Content = incident.Type },
                ThreatLevel = new UpdatePropertyInt { Content = incident.Threat },
                DetectionDescription = new UpdatePropertyString
                { Content = $"Reported by PATROL 1 near route BRAVO ({incident.Name.ToLowerInvariant()})" },
                Location = new UpdatePropertyLocation { Content = location }
            }
        };
    }

    /// <summary>An acoustic/optical sensor detection arc from an observation post - a Fan location.</summary>
    private SymbolLocation SensorArcLocation(Timestamp nowTs, double lat, double lon)
    {
        return new SymbolLocation
        {
            Fan = new Fan
            {
                LocationTime = nowTs,
                Name = "Detection arc",
                VertexPoint = Geo(lat, lon),
                OrientationAngle = _random.NextDouble() * 360,
                SectorSizeAngle = 40 + _random.NextDouble() * 30,
                MinimumRangeDimension = 50,
                MaximumRangeDimension = 300 + _random.NextDouble() * 300
            }
        };
    }

    /// <summary>An artillery impact area (CEP-style) - an Ellipse elongated along a random impact axis.</summary>
    private SymbolLocation ImpactAreaLocation(Timestamp nowTs, double lat, double lon)
    {
        var bearing = _random.NextDouble() * 360;
        var semiMajorM = 150 + _random.NextDouble() * 100;
        var semiMinorM = 60 + _random.NextDouble() * 60;
        var major = Destination(lat, lon, bearing, semiMajorM);
        var minor = Destination(lat, lon, bearing + 90, semiMinorM);

        return new SymbolLocation
        {
            Ellipse = new Ellipse
            {
                LocationTime = nowTs,
                Name = "Impact area",
                CenterPoint = Geo(lat, lon),
                FirstConjugateDiameterPoint = Geo(major.Lat, major.Lon),
                SecondConjugateDiameterPoint = Geo(minor.Lat, minor.Lon)
            }
        };
    }

    /// <summary>A cluster of discovered devices near a single anchor point - a Multipoint location.</summary>
    private SymbolLocation DeviceClusterLocation(Timestamp nowTs, double lat, double lon)
    {
        var multipoint = new Multipoint { LocationTime = nowTs, Name = "Device cluster" };
        var count = 4 + _random.Next(3);
        for (var i = 0; i < count; i++)
        {
            var bearing = _random.NextDouble() * 360;
            var distance = 15 + _random.NextDouble() * 60;
            var point = Destination(lat, lon, bearing, distance);
            multipoint.Points.Add(Geo(point.Lat, point.Lon));
        }

        return new SymbolLocation { Multipoint = multipoint };
    }

    private UpdateSituationObject SitrepChat(Identity reporter, Timestamp nowTs, DateTimeOffset now)
    {
        string[] messages =
        [
            "PATROL 1: checkpoint reached, all clear.",
            "PATROL 1: continuing on route BRAVO.",
            "HQ: acknowledged, maintain current speed.",
            "PATROL 1: visibility good, no contacts.",
            "HQ: weather update - rain expected in 2 hours."
        ];
        var id = $"scenario:chat:{Interlocked.Increment(ref _chatCounter):D5}";
        var text = messages[_random.Next(messages.Length)];

        return new UpdateSituationObject
        {
            TextDocument = new UpdateTextDocument
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(now.AddMinutes(30)) },
                Name = new UpdatePropertyString { Content = "SITREP" },
                PlainContent = new UpdatePropertyString { Content = text },
                Content = new UpdatePropertyString { Content = text },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine }
            }
        };
    }

    private static UpdateSituationObject NatoMessage(Identity reporter, Timestamp nowTs, DateTimeOffset now)
    {
        return new UpdateSituationObject
        {
            NatoMessageDocument = new UpdateNatoMessageDocument
            {
                Identity = new Identity { StringIdentity = "scenario:nato:ownsitrep" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "OWNSITREP A Coy" },
                MtfMessageData = new UpdatePropertyString
                {
                    Content = "MSGID/OWNSITREP/A-COY//\n" +
                              $"DTG/{now:ddHHmm}Z{now:MMMyy}//\n" +
                              "OWNSIT/A COY CONDUCTING ROUTE PATROL BRAVO/STATUS GREEN//"
                },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Priority }
            }
        };
    }

    private static UpdateSituationObject ReconPicture(Identity reporter, Timestamp nowTs, RouteWaypoint at)
    {
        return new UpdateSituationObject
        {
            PictureDocument = new UpdatePictureDocument
            {
                Identity = new Identity { StringIdentity = "scenario:picture:recon1" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Recon photo WP2" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Bridge at waypoint 2, structurally intact" },
                PictureData =
                    new UpdatePropertyByteArray { Content = ByteString.CopyFrom(TinyPng), Type = "image/png" },
                LowResPictureData = new UpdatePropertyByteArray
                { Content = ByteString.CopyFrom(TinyPng), Type = "image/png" },
                DirectionOfView = new UpdatePropertyInt { Content = 135 },
                FocalLength = new UpdatePropertyInt { Content = 50 },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine },
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point { LocationTime = nowTs, GeoPoint = Geo(at.Lat, at.Lon) }
                    }
                }
            }
        };
    }

    private static UpdateSituationObject RadioCheck(Identity reporter, Timestamp nowTs)
    {
        return new UpdateSituationObject
        {
            VoiceMessageDocument = new UpdateVoiceMessageDocument
            {
                Identity = new Identity { StringIdentity = "scenario:voice:radiocheck" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Radio check PATROL 1" },
                SoundFile = new UpdatePropertyByteArray { Content = ByteString.CopyFrom(TinyWav), Type = "audio/wav" },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Normal },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine }
            }
        };
    }

    /// <summary>
    ///     A multi-element planning sketch: the objective outline (dashed, filled), the axis of
    ///     advance from the start point to the objective (solid), and the objective's rally point
    ///     (a marker) - each sketch element individually colored/styled.
    /// </summary>
    private static UpdateSituationObject PlanningSketch(Identity reporter, Timestamp nowTs, ScenarioGeometry geo)
    {
        var sketch = new SketchLocation { LocationTime = nowTs, Name = "Approach sketch" };

        var objectivePolygon = new Polygon { Name = "Objective outline" };
        foreach (var (lat, lon) in geo.ObjectiveArea) objectivePolygon.Points.Add(Geo(lat, lon));
        sketch.Elements.Add(new SketchLocationElement
        {
            Location = new SymbolLocation { Polygon = objectivePolygon },
            LineColor = new Color { Red = 220, Green = 50, Blue = 50, Alpha = 255 },
            LineWidth = 2,
            LineStyle = LineStyle.Dash,
            FillStyle = FillStyle.DiagonalCross
        });

        var axisOfAdvance = new Line { Name = "Axis of advance" };
        axisOfAdvance.Points.Add(Geo(geo.PatrolWaypoints[0].Lat, geo.PatrolWaypoints[0].Lon));
        axisOfAdvance.Points.Add(Geo(geo.ObjectiveCentroid.Lat, geo.ObjectiveCentroid.Lon));
        sketch.Elements.Add(new SketchLocationElement
        {
            Location = new SymbolLocation { Line = axisOfAdvance },
            LineColor = new Color { Red = 0, Green = 128, Blue = 255, Alpha = 255 },
            LineWidth = 3,
            LineStyle = LineStyle.Solid
        });

        sketch.Elements.Add(new SketchLocationElement
        {
            Location = new SymbolLocation
            {
                Point = new Point
                {
                    Name = "Rally point",
                    GeoPoint = Geo(geo.ObjectiveCentroid.Lat, geo.ObjectiveCentroid.Lon)
                }
            },
            LineColor = new Color { Red = 240, Green = 200, Blue = 0, Alpha = 255 }
        });

        return new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = "scenario:sketch:approach" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Approach sketch" },
                AdditionalInformation = new UpdatePropertyString
                { Content = "Planned approach for phase 3: objective outline, axis of advance, rally point" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { SketchLocation = sketch } },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine }
            }
        };
    }

    /// <summary>Overlay with two nested phase-line symbols as overlay content.</summary>
    private static UpdateSituationObject PhaseLineOverlay(
        Identity reporter, Timestamp nowTs, RouteWaypoint[] waypoints)
    {
        var overlayData = new UpdatePropertySituationObjects();
        overlayData.Contents.Add(PhaseLineSymbol(reporter, nowTs, "scenario:overlay:pl-alpha", "PL ALPHA", waypoints[0],
            waypoints[1]));
        overlayData.Contents.Add(PhaseLineSymbol(reporter, nowTs, "scenario:overlay:pl-bravo", "PL BRAVO", waypoints[3],
            waypoints[2]));

        return new UpdateSituationObject
        {
            OverlayDocument = new UpdateOverlayDocument
            {
                Identity = new Identity { StringIdentity = "scenario:overlay:phaselines" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Phase lines" },
                Tag = new UpdatePropertyString { Content = "PLANNING" },
                OverlayData = overlayData,
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine }
            }
        };
    }

    private static UpdateSituationObject PhaseLineSymbol(
        Identity reporter, Timestamp nowTs, string id, string name, RouteWaypoint from, RouteWaypoint to)
    {
        var line = new Line { LocationTime = nowTs, Name = name };
        line.Points.Add(Geo(from.Lat, from.Lon));
        line.Points.Add(Geo(to.Lat, to.Lon));

        return new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = name },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Line = line } },
                SymbolIdentifier = new UpdatePropertySymbolIdentifier
                {
                    Content = new SymbolIdentifier
                    {
                        SymbolCatalog = SymbolCatalog.Mil2525C,
                        StringIdentifier = "GFGPGLP--------"
                    }
                }
            }
        };
    }

    private static byte[] CreateTinyWav()
    {
        const int sampleRate = 8000;
        const int samples = 8; // a few silent samples keep the file minimal but valid
        var dataSize = samples;
        var wav = new byte[44 + dataSize];

        void WriteAscii(int offset, string text)
        {
            for (var i = 0; i < text.Length; i++) wav[offset + i] = (byte)text[i];
        }

        void WriteInt32(int offset, int value)
        {
            BitConverter.TryWriteBytes(wav.AsSpan(offset), value);
        }

        void WriteInt16(int offset, short value)
        {
            BitConverter.TryWriteBytes(wav.AsSpan(offset), value);
        }

        WriteAscii(0, "RIFF");
        WriteInt32(4, 36 + dataSize);
        WriteAscii(8, "WAVE");
        WriteAscii(12, "fmt ");
        WriteInt32(16, 16);
        WriteInt16(20, 1); // PCM
        WriteInt16(22, 1); // mono
        WriteInt32(24, sampleRate);
        WriteInt32(28, sampleRate); // byte rate (8-bit mono)
        WriteInt16(32, 1); // block align
        WriteInt16(34, 8); // bits per sample
        WriteAscii(36, "data");
        WriteInt32(40, dataSize);
        for (var i = 0; i < samples; i++) wav[44 + i] = 128; // 8-bit silence

        return wav;
    }
}
