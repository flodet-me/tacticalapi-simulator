using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Offline source that exercises ALL 11 situation object types of the
///     TacticalAPI v0 contract in one coherent mini scenario:
///     - OrganizationUnit: a company HQ with two subordinated platoons (ORBAT)
///     - Route:            a rectangular patrol route (Line location)
///     - Symbol:           a patrol vehicle moving along that route
///     - ActionTask:       the patrol order, progressing NOT_STARTED ->
///     IN_PROGRESS -> COMPLETE with a live completion ratio
///     - ActionEvent:      random incidents near the route (expire via TTL)
///     - TextDocument:     periodic SITREP chat messages
///     - NatoMessageDocument: an OWNSITREP MTF message
///     - PictureDocument:  recon photo (tiny embedded PNG)
///     - VoiceMessageDocument: radio check (tiny embedded WAV)
///     - SketchDocument:   a planning sketch along the route
///     - OverlayDocument:  an overlay carrying two nested phase-line symbols
///     Everything is emitted as plain UpdateSituationObject batches through the
///     standard ingest path - exactly like an external TacticalAPI client.
/// </summary>
public sealed class SyntheticScenarioSource(
    IOptionsMonitor<SyntheticScenarioOptions> options,
    TimeProvider timeProvider)
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

    private readonly Random _random = new(options.CurrentValue.Seed);
    private int _chatCounter;
    private int _eventCounter;

    public string Name => "SyntheticScenario";

    public bool Enabled => options.CurrentValue.Enabled;

    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);
        var reporter = new Identity { StringIdentity = o.ReporterId };
        var waypoints = PatrolWaypoints(o);

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

            // --- Patrol route (Line location) --------------------------------
            Route(reporter, nowTs, waypoints),

            // --- Patrol vehicle moving along the route ------------------------
            PatrolSymbol(o, now, waypoints),

            // --- The patrol order with live progress --------------------------
            PatrolTask(o, reporter, now, nowTs, waypoints),

            // --- Documents ----------------------------------------------------
            NatoMessage(reporter, nowTs, now),
            ReconPicture(reporter, nowTs, waypoints[1]),
            RadioCheck(reporter, nowTs),
            PlanningSketch(reporter, nowTs, waypoints),
            PhaseLineOverlay(reporter, nowTs, waypoints)
        };

        // --- Spontaneous incidents (expire automatically via TTL) -------------
        if (_random.NextDouble() < o.EventProbability) updates.Add(RandomIncident(o, reporter, now, nowTs, waypoints));

        // --- SITREP chat -------------------------------------------------------
        if (_random.NextDouble() < o.ChatProbability) updates.Add(SitrepChat(reporter, nowTs, now));

        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Four corners of a rectangular patrol route around the center.</summary>
    private static (double Lat, double Lon)[] PatrolWaypoints(SyntheticScenarioOptions o)
    {
        var dLat = o.ExtentKm / 2 / EarthRadiusKm * (180.0 / Math.PI);
        var dLon = dLat / Math.Cos(o.CenterLatitude * Math.PI / 180.0);
        return
        [
            (o.CenterLatitude - dLat, o.CenterLongitude - dLon),
            (o.CenterLatitude - dLat, o.CenterLongitude + dLon),
            (o.CenterLatitude + dLat, o.CenterLongitude + dLon),
            (o.CenterLatitude + dLat, o.CenterLongitude - dLon)
        ];
    }

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

    private static UpdateSituationObject Route(
        Identity reporter, Timestamp nowTs, (double Lat, double Lon)[] waypoints)
    {
        var line = new Line { LocationTime = nowTs, Name = "Patrol route BRAVO" };
        foreach (var (lat, lon) in waypoints.Append(waypoints[0]))
            line.Points.Add(new GeoPoint { LatitudeCoordinate = lat, LongitudeCoordinate = lon });

        return new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "scenario:route:bravo" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Route BRAVO" },
                AdditionalInformation = new UpdatePropertyString { Content = "Daily patrol loop" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Line = line } },
                MarchSpeed = new UpdatePropertyInt { Content = 40 },
                LineColor = new UpdatePropertyColor
                { Content = new Color { Red = 0, Green = 128, Blue = 255, Alpha = 255 } },
                LineWidth = new UpdatePropertyInt { Content = 3 },
                LineStyle = new UpdatePropertyLineStyle { Content = LineStyle.Dash },
                RouteType = new UpdatePropertyRouteType { Content = RouteType.MainSupplyRoute }
            }
        };
    }

    /// <summary>Patrol vehicle position interpolated along the route perimeter.</summary>
    private UpdateSituationObject PatrolSymbol(
        SyntheticScenarioOptions o, DateTimeOffset now, (double Lat, double Lon)[] waypoints)
    {
        var (lat, lon, course) = PositionOnPerimeter(o, now, waypoints);
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
        SyntheticScenarioOptions o, DateTimeOffset now, (double Lat, double Lon)[] wp)
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
        SyntheticScenarioOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs,
        (double Lat, double Lon)[] waypoints)
    {
        var lapFraction = (now - _epoch).TotalSeconds / o.PatrolLapDuration.TotalSeconds % 1.0;
        var status = lapFraction switch
        {
            < 0.02 => ActionTaskStatusType.NotStarted,
            > 0.98 => ActionTaskStatusType.Complete,
            _ => ActionTaskStatusType.InProgress
        };

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
                        GeoPoint = new GeoPoint
                        { LatitudeCoordinate = waypoints[0].Lat, LongitudeCoordinate = waypoints[0].Lon }
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
        SyntheticScenarioOptions o, Identity reporter, DateTimeOffset now, Timestamp nowTs,
        (double Lat, double Lon)[] waypoints)
    {
        (ActionEventType Type, string Name, int Threat)[] catalog =
        [
            (ActionEventType.SniperAttack, "Sniper attack", 4),
            (ActionEventType.ArtilleryFire, "Artillery fire observed", 5),
            (ActionEventType.BoobyTrapDiscovery, "Booby trap discovered", 3),
            (ActionEventType.AccidentTraffic, "Traffic accident", 1),
            (ActionEventType.FixAcoustic, "Acoustic fix", 2)
        ];
        var incident = catalog[_random.Next(catalog.Length)];
        var anchor = waypoints[_random.Next(waypoints.Length)];
        var lat = anchor.Lat + (_random.NextDouble() - 0.5) * 0.02;
        var lon = anchor.Lon + (_random.NextDouble() - 0.5) * 0.02;
        var id = $"scenario:event:{Interlocked.Increment(ref _eventCounter):D5}";

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
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point
                        {
                            LocationTime = nowTs,
                            GeoPoint = new GeoPoint { LatitudeCoordinate = lat, LongitudeCoordinate = lon }
                        }
                    }
                }
            }
        };
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

    private static UpdateSituationObject ReconPicture(
        Identity reporter, Timestamp nowTs, (double Lat, double Lon) at)
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
                        Point = new Point
                        {
                            LocationTime = nowTs,
                            GeoPoint = new GeoPoint { LatitudeCoordinate = at.Lat, LongitudeCoordinate = at.Lon }
                        }
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

    private static UpdateSituationObject PlanningSketch(
        Identity reporter, Timestamp nowTs, (double Lat, double Lon)[] waypoints)
    {
        var line = new Line { LocationTime = nowTs, Name = "Approach sketch" };
        line.Points.Add(new GeoPoint { LatitudeCoordinate = waypoints[0].Lat, LongitudeCoordinate = waypoints[0].Lon });
        line.Points.Add(new GeoPoint { LatitudeCoordinate = waypoints[2].Lat, LongitudeCoordinate = waypoints[2].Lon });

        return new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = "scenario:sketch:approach" },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = "Approach sketch" },
                AdditionalInformation = new UpdatePropertyString { Content = "Planned approach for phase 2" },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Line = line } },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Routine }
            }
        };
    }

    /// <summary>Overlay with two nested phase-line symbols as overlay content.</summary>
    private static UpdateSituationObject PhaseLineOverlay(
        Identity reporter, Timestamp nowTs, (double Lat, double Lon)[] waypoints)
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
        Identity reporter, Timestamp nowTs, string id, string name,
        (double Lat, double Lon) from, (double Lat, double Lon) to)
    {
        var line = new Line { LocationTime = nowTs, Name = name };
        line.Points.Add(new GeoPoint { LatitudeCoordinate = from.Lat, LongitudeCoordinate = from.Lon });
        line.Points.Add(new GeoPoint { LatitudeCoordinate = to.Lat, LongitudeCoordinate = to.Lon });

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
