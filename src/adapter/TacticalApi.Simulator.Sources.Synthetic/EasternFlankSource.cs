using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Theater-scale demo picture: a front line running from the Gulf of Finland down to the Black
///     Sea, formations deployed along both sides of it, and strategic reinforcement flows feeding
///     each side - transports crossing the Atlantic to the western rear, rail movements coming the
///     long way from the far east to the eastern rear.
///     The front is not static. One side breaks through, the line bulges deep into the other's
///     territory and the ground it took is drawn as a captured area; the defender pushes it back;
///     then the other side does the same at a different sector. A full swing takes
///     <see cref="EasternFlankOptions.CycleDuration" /> and then repeats, so the picture tells a
///     story on its own for as long as a meeting lasts.
///     Fictional: the geography is real, the forces, formations and movements are invented for the
///     demo. Everything is a function of elapsed time only - no RNG - so the same demo runs the
///     same way twice.
/// </summary>
public sealed class EasternFlankSource(
    IOptionsMonitor<EasternFlankOptions> options,
    TimeProvider timeProvider,
    ILogger<EasternFlankSource> logger)
    : ISimulationSource
{
    // MIL-STD-2525C, 15 characters each, for the handful of types that don't come from a table
    // below. "F" is the western side's affiliation, "H" the eastern one's.
    private const string WesternSupply = "SFGPUSS--------";
    private const string WesternSea = "SFSP-----------";
    private const string WesternHeadquarters = "SFGPUH---------";
    private const string EasternSupply = "SHGPUSS--------";
    private const string EasternSea = "SHSP-----------";
    private const string EasternHeadquarters = "SHGPUH---------";
    private const string WesternSatellite = "SFPPS----------";
    private const string EasternSatellite = "SHPPS----------";
    private const string NeutralSea = "SNSP-----------";
    private const string NeutralGround = "SNGPU----------";
    private const string UnknownAir = "SUAPMF---------";
    private const string UnknownGround = "SUGPU----------";

    /// <summary>
    ///     The branches shown along the front, cycled per formation so the line reads as a combined
    ///     arms force rather than a row of identical icons. The strings are MIL-STD-2525C
    ///     warfighting function identifiers; only the affiliation character differs per side.
    /// </summary>
    private static readonly (string Function, string Role)[] GroundBranches =
    [
        ("UCA", "Armor battalion"),
        ("UCIZ", "Mechanized infantry battalion"),
        ("UCF", "Field artillery battalion"),
        ("UCR", "Reconnaissance squadron"),
        ("UCD", "Air defense battery"),
        ("UCFR", "Rocket artillery battery"),
        ("UCE", "Engineer battalion"),
        ("UCM", "Missile battalion"),
        ("UCAA", "Armored cavalry squadron"),
        ("UUS", "Signal battalion"),
        ("UUM", "Military intelligence company"),
        ("USM", "Medical company"),
        ("UST", "Transport battalion"),
        ("USS", "Supply battalion")
    ];

    /// <summary>Air types cycled through the patrols, so the air picture isn't one repeated icon.</summary>
    private static readonly (string Function, string Role, string Callsign, double Altitude, double Speed)[] AirTypes =
    [
        ("MFF", "Fighter patrol", "VIPER", 9000, 230),
        ("MFA", "Airborne early warning", "MAGIC", 10000, 200),
        ("MFK", "Air-to-air refuelling", "SHELL", 8000, 190),
        ("MHA", "Attack helicopter flight", "HAMMER", 300, 60),
        ("MFQ", "Unmanned reconnaissance", "SHADOW", 6000, 120),
        ("MFB", "Bomber flight", "ANVIL", 11000, 240)
    ];

    private static readonly (int R, int G, int B) WesternColor = (0, 110, 220);
    private static readonly (int R, int G, int B) EasternColor = (200, 40, 40);
    private static readonly (int R, int G, int B) NeutralColor = (245, 180, 0);

    // Line weights in pixels, graded by how binding the line is: the FLOT is the heaviest thing on
    // the map, the pre-conflict border the lightest - it is there for reference, not as a control
    // measure. Kept inside 1-10, which is the range the TAK adapter clamps line widths to.
    private const uint ForwardLineWidth = 8;
    private const uint EngagementZoneWidth = 6;
    private const uint AirspaceWidth = 5;
    private const uint PhaseLineWidth = 4;
    private const uint CapturedAreaWidth = 3;
    private const uint AreaOfInterestWidth = 3;
    private const uint AirDefenseWidth = 2;
    private const uint StagingAreaWidth = 1;
    private const uint BorderWidth = 1;

    /// <summary>How far along the front a bulge reaches to either side of its center, in nodes.</summary>
    private const double BulgeSpread = 1.9;

    /// <summary>Distance the deployed formations sit behind their own side of the line.</summary>
    private const double DeploymentDepthKm = 45;

    // 1x1 transparent PNG - a minimal valid payload for the incident pictures.
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private readonly DateTimeOffset _epoch = timeProvider.GetUtcNow();

    /// <inheritdoc />
    public string Name => EasternFlankOptions.Name;

    /// <inheritdoc />
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc />
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <summary>
    ///     Produces the whole theater every cycle. The static frame (borders, zones, routes, ORBAT)
    ///     is re-reported unchanged and merged away as no-ops; the front line, the captured areas
    ///     and every formation follow the current phase of the swing.
    /// </summary>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var nowTs = Timestamp.FromDateTimeOffset(now);
        var reporter = new Identity { StringIdentity = settings.ReporterId };
        var elapsed = now - _epoch;

        var phase = Fraction(elapsed, settings.CycleDuration);
        var easternPush = phase < 0.5 ? Math.Sin(Math.PI * phase / 0.5) : 0;
        var westernPush = phase >= 0.5 ? Math.Sin(Math.PI * (phase - 0.5) / 0.5) : 0;
        var (front, displacementKm) = CurrentFront(settings, phase, elapsed);

        var updates = new List<UpdateSituationObject>
        {
            // --- ORBAT ---------------------------------------------------------
            // UnitDesignation stops at Regiment in the contract, so the echelon above that is
            // carried by the formation's name rather than by the enum.
            Unit("flank:unit:west:corps", "Multinational Corps North East", UnitDesignation.Regiment, "SHAPE",
                ["flank:unit:west:north", "flank:unit:west:south"], WesternColor, WesternHeadquarters, reporter,
                nowTs),
            Unit("flank:unit:west:north", "Baltic Division", UnitDesignation.Battalion,
                "Multinational Corps North East", [], WesternColor, WesternHeadquarters, reporter, nowTs),
            Unit("flank:unit:west:south", "Southern Flank Division", UnitDesignation.Battalion,
                "Multinational Corps North East", [], WesternColor, WesternHeadquarters, reporter, nowTs),
            Unit("flank:unit:east:group", "Western Group of Forces", UnitDesignation.Regiment, "General Staff",
                ["flank:unit:east:north", "flank:unit:east:south"], EasternColor, EasternHeadquarters, reporter,
                nowTs),
            Unit("flank:unit:east:north", "Northern Operational Command", UnitDesignation.Battalion,
                "Western Group of Forces", [], EasternColor, EasternHeadquarters, reporter, nowTs),
            Unit("flank:unit:east:south", "Southern Operational Command", UnitDesignation.Battalion,
                "Western Group of Forces", [], EasternColor, EasternHeadquarters, reporter, nowTs),

            // --- The line itself ------------------------------------------------
            SketchBuilder.Document("flank:line:border", "Pre-conflict border",
                "Internationally recognised border, for reference", reporter, nowTs,
                SketchBuilder.Element(
                    SketchBuilder.Line(nowTs, "Pre-conflict border",
                        EasternFlankGeometry.Front.Select(n => (n.Lat, n.Lon)).ToArray()),
                    NeutralColor, BorderWidth, LineStyle.Dot)),
            SketchBuilder.Document("flank:line:flot", "FLOT", "Forward line of own troops, current trace", reporter,
                nowTs,
                SketchBuilder.Element(SketchBuilder.Line(nowTs, "FLOT", front), EasternColor, ForwardLineWidth,
                    LineStyle.Solid)),

            // --- Reinforcement routes -------------------------------------------
            Route("flank:route:atlantic", "SLOC AMBER", "Transatlantic reinforcement route, Norfolk to Warsaw",
                EasternFlankGeometry.TransatlanticRoute, WesternColor, RouteType.MainSupplyRoute, reporter, nowTs),
            Route("flank:route:eastern", "LOC GRANITE", "Eastern reserve movement, rail from the far east",
                EasternFlankGeometry.EasternReserveRoute, EasternColor, RouteType.MainSupplyRoute, reporter, nowTs),
            Route("flank:route:southern", "LOC SAFFRON",
                "Southern supply line, Tehran to Gomel via the Caspian and the Volga corridor",
                EasternFlankGeometry.SouthernSupplyRoute, EasternColor, RouteType.AlternateSupplyRoute, reporter,
                nowTs),

            // --- Situation reporting --------------------------------------------
            FrontTask(settings, reporter, nowTs, phase, easternPush, westernPush),
            Sitrep(reporter, nowTs, now, phase, easternPush, westernPush)
        };

        // --- Zones and areas ----------------------------------------------------
        updates.AddRange(Zones("flank:zone:east:ad", EasternFlankGeometry.EasternAirDefense,
            "Air defense umbrella", EasternColor, AirDefenseWidth, LineStyle.Dash, reporter, nowTs));
        updates.AddRange(Zones("flank:zone:west:ad", EasternFlankGeometry.WesternAirDefense,
            "Integrated air defense", WesternColor, AirDefenseWidth, LineStyle.Dash, reporter, nowTs));
        updates.AddRange(Zones("flank:zone:east:staging", EasternFlankGeometry.EasternStagingAreas,
            "Staging area", EasternColor, StagingAreaWidth, LineStyle.Dot, reporter, nowTs));
        updates.AddRange(Zones("flank:zone:west:staging", EasternFlankGeometry.WesternStagingAreas,
            "Staging area", WesternColor, StagingAreaWidth, LineStyle.Dot, reporter, nowTs));
        updates.AddRange(ControlMeasures(reporter, nowTs));

        var holdThresholdKm = Math.Max(5, settings.MaxBulgeKm * 0.15);
        var easternGain = CapturedArea(front, displacementKm, holdThresholdKm, true);
        if (easternGain.Length >= 4)
            updates.Add(SketchBuilder.Document("flank:area:east-gain", "Occupied territory (east)",
                "Ground taken by the eastern breakthrough", reporter, nowTs,
                SketchBuilder.Element(SketchBuilder.Polygon(nowTs, "Occupied territory (east)", easternGain),
                    EasternColor, CapturedAreaWidth, LineStyle.Solid)));

        var westernGain = CapturedArea(front, displacementKm, holdThresholdKm, false);
        if (westernGain.Length >= 4)
            updates.Add(SketchBuilder.Document("flank:area:west-gain", "Liberated territory (west)",
                "Ground retaken by the western counter-offensive", reporter, nowTs,
                SketchBuilder.Element(SketchBuilder.Polygon(nowTs, "Liberated territory (west)", westernGain),
                    WesternColor, CapturedAreaWidth, LineStyle.Solid)));

        // --- Forces --------------------------------------------------------------
        updates.AddRange(FrontForces(settings, reporter, now, nowTs, elapsed, front, true, easternPush));
        updates.AddRange(FrontForces(settings, reporter, now, nowTs, elapsed, front, false, westernPush));
        updates.AddRange(SpecialForces(settings, now, elapsed));
        updates.AddRange(Satellites(settings, now, elapsed));
        updates.AddRange(UnknownContacts(settings, now, elapsed));
        updates.AddRange(NeutralTraffic(settings, now, elapsed));
        updates.AddRange(Installations(settings, now));
        updates.AddRange(Reinforcements(settings, now, elapsed, EasternFlankGeometry.TransatlanticRoute,
            "flank:reinf:west", "CONVOY", WesternSea, WesternSupply,
            EasternFlankGeometry.TransatlanticLastSeaLeg, settings.UsReinforcementCount,
            "Transatlantic reinforcement, en route to the front"));
        updates.AddRange(Reinforcements(settings, now, elapsed, EasternFlankGeometry.EasternReserveRoute,
            "flank:reinf:east", "ECHELON", EasternSupply, EasternSupply, int.MaxValue,
            settings.EasternReserveCount, "Strategic reserve, rail movement to the western theater"));
        updates.AddRange(Reinforcements(settings, now, elapsed, EasternFlankGeometry.SouthernSupplyRoute,
            "flank:reinf:south", "SAFFRON", EasternSea, EasternSupply,
            EasternFlankGeometry.SouthernSupplyLastSeaLeg, settings.SouthernSupplyCount,
            "Southern supply line, materiel from Iran across the Caspian"));
        updates.AddRange(IncidentReports(settings, reporter, now, nowTs, elapsed));
        updates.AddRange(AirPatrols(settings, now, elapsed, front));
        updates.AddRange(NavalGroups(settings, now, elapsed));

        // --- Events at the peak of each push -------------------------------------
        if (easternPush > 0.6)
            updates.Add(Event(settings, "flank:event:breakthrough", "Breakthrough POLAND EAST",
                "Armored breakthrough through the Polish border sector, defenders falling back", 5,
                front[EasternFlankGeometry.EasternBreakthroughIndex], reporter, now, nowTs));

        if (westernPush > 0.6)
            updates.Add(Event(settings, "flank:event:counter", "Counter-offensive LATVIA EAST",
                "Western counter-offensive has crossed the border, eastern forces withdrawing", 4,
                front[EasternFlankGeometry.WesternCounterIndex], reporter, now, nowTs));

        // Last, so nothing reported above can leave without one.
        RefreshExpiry(updates, now, settings.TrackTimeToLive);

        logger.EasternFlankProduced(updates.Count, (int)(phase * 100));
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }

    /// <summary>
    ///     Gives every object reported this cycle an expiry of <paramref name="now" /> +
    ///     <paramref name="timeToLive" />, unless it set one of its own.
    ///     This is what keeps the picture alive on a client that ages objects out by their expiry
    ///     time - a TAK client's stale time, for instance. Without it the objects that never change
    ///     - the ORBAT, the routes, the border, the zones, the fixed installations - are reported
    ///     once and then look to such a client like something nobody has heard from since, and get
    ///     dropped off the map while the simulator is still faithfully re-reporting them. Because
    ///     the stamp moves forward every cycle it is also, by itself, a real change on an otherwise
    ///     identical object, so a consumer that suppresses no-op updates still sees a heartbeat.
    ///     The objects that manage their own lifetime keep it: a formation reported destroyed
    ///     carries an expiry in the past so the situation deletes it, and the loss reports, incident
    ///     pictures and SITREP each have a window of their own.
    /// </summary>
    private static void RefreshExpiry(List<UpdateSituationObject> updates, DateTimeOffset now, TimeSpan timeToLive)
    {
        var expiry = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(now + timeToLive) };

        foreach (var update in updates)
            switch (update.TypeCase)
            {
                case UpdateSituationObject.TypeOneofCase.Symbol:
                    update.Symbol.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.OrganizationUnit:
                    update.OrganizationUnit.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.Route:
                    update.Route.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.SketchDocument:
                    update.SketchDocument.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.ActionTask:
                    update.ActionTask.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.ActionEvent:
                    update.ActionEvent.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.TextDocument:
                    update.TextDocument.ExpiryTime ??= expiry;
                    break;
                case UpdateSituationObject.TypeOneofCase.PictureDocument:
                    update.PictureDocument.ExpiryTime ??= expiry;
                    break;
                default:
                    break;
            }
    }

    // -------------------------------------------------------------- the front line

    /// <summary>
    ///     The current trace of the front: every baseline node displaced perpendicular to the line,
    ///     westwards where the eastern side is pushing and eastwards where the western side is.
    ///     The displacement falls off with distance from the breakthrough's center node, which is
    ///     what gives the line its bulge instead of a step.
    /// </summary>
    private static ((double Lat, double Lon)[] Points, double[] DisplacementKm) CurrentFront(
        EasternFlankOptions settings, double phase, TimeSpan elapsed)
    {
        var front = new (double Lat, double Lon)[EasternFlankGeometry.Front.Length];
        var displacement = new double[front.Length];

        for (var i = 0; i < front.Length; i++)
        {
            var node = EasternFlankGeometry.Front[i];

            // Every push that is running right now contributes to this node, so two efforts in
            // different sectors bend the line in different places at the same time.
            var displacementKm = 0.0;
            foreach (var bulge in EasternFlankGeometry.Bulges)
            {
                var strength = BulgeStrength(bulge, phase) * Falloff(i, bulge.CenterIndex, bulge.Spread);
                if (strength <= 0) continue;

                var depth = settings.MaxBulgeKm * bulge.Depth * strength;
                displacementKm += bulge.Side == Allegiance.Eastern ? depth : -depth;
            }

            // A slow ripple along the whole line, so even quiet sectors creep back and forth
            // instead of sitting on the border like a drawn boundary.
            displacementKm += Math.Sin(elapsed.TotalSeconds / 23.0 + i * 0.9) * settings.MaxBulgeKm * 0.06;
            displacement[i] = displacementKm;

            if (Math.Abs(displacementKm) < 0.5)
            {
                front[i] = (node.Lat, node.Lon);
                continue;
            }

            // Positive displacement is westward - ground the eastern side has taken; negative is
            // eastward, taken by the western side.
            var bearing = EasternFlankGeometry.WestwardBearing(i);
            if (displacementKm < 0) bearing = (bearing + 180) % 360;

            front[i] = GeoMath.Destination(node.Lat, node.Lon, bearing, Math.Abs(displacementKm) * 1000);
        }

        return (front, displacement);
    }

    /// <summary>How much of a push's depth reaches the node <paramref name="index" />.</summary>
    private static double Falloff(int index, int center, double spread = BulgeSpread)
    {
        var distance = index - center;
        return Math.Exp(-(distance * distance) / (2 * spread * spread));
    }

    /// <summary>
    ///     How hard a push is going right now: nothing outside its window, a smooth rise and fall
    ///     inside it. Each push has its own window, so they start and finish at different times.
    /// </summary>
    private static double BulgeStrength(BulgeProfile bulge, double phase)
    {
        if (phase < bulge.StartPhase || phase > bulge.EndPhase) return 0;

        var window = bulge.EndPhase - bulge.StartPhase;
        if (window <= 0) return 0;

        return Math.Sin(Math.PI * (phase - bulge.StartPhase) / window);
    }

    /// <summary>
    ///     The ground one side currently holds beyond the border: the displaced front on one side,
    ///     the border on the other, closed into a polygon. Empty while that side isn't ahead.
    /// </summary>
    private static (double Lat, double Lon)[] CapturedArea((double Lat, double Lon)[] front, double[] displacementKm,
        double thresholdKm, bool eastern)
    {
        var indices = new List<int>();
        for (var i = 0; i < front.Length; i++)
        {
            // The sign of the displacement says whose gain the node is, so no geometry is needed
            // to work out which side of the border it ended up on.
            // The threshold is above the amplitude of the idle ripple, so a quiet sector never
            // counts as captured ground.
            var ahead = eastern ? displacementKm[i] > thresholdKm : displacementKm[i] < -thresholdKm;
            if (ahead) indices.Add(i);
        }

        if (indices.Count < 2) return [];

        var area = new List<(double Lat, double Lon)>();
        foreach (var i in indices) area.Add(front[i]);
        for (var i = indices.Count - 1; i >= 0; i--)
        {
            var node = EasternFlankGeometry.Front[indices[i]];
            area.Add((node.Lat, node.Lon));
        }

        return area.ToArray();
    }

    // ------------------------------------------------------------------- forces

    /// <summary>
    ///     The formations deployed along one side of the front: spread evenly along the current
    ///     trace, set back from it, and pressing forward while their side is attacking.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> FrontForces(EasternFlankOptions settings, Identity reporter,
        DateTimeOffset now, Timestamp nowTs, TimeSpan elapsed, (double Lat, double Lon)[] front, bool eastern,
        double push)
    {
        var names = eastern ? EasternFlankGeometry.EasternFormations : EasternFlankGeometry.WesternFormations;
        var prefix = eastern ? "flank:force:east" : "flank:force:west";
        var lost = DestroyedIndex(settings, elapsed, eastern);

        for (var i = 0; i < settings.FrontUnitsPerSide; i++)
        {
            var alongFront = (i + 0.5) / settings.FrontUnitsPerSide;
            var (onLine, index) = AlongFront(front, alongFront);

            // Deployed behind their own side of the line, facing the other.
            var westward = EasternFlankGeometry.WestwardBearing(index);
            var toOwnRear = eastern ? (westward + 180) % 360 : westward;
            var point = GeoMath.Destination(onLine.Lat, onLine.Lon, toOwnRear, DeploymentDepthKm * 1000);

            // Even a holding formation shifts between reports - a picture where half the symbols
            // never move at all reads as frozen rather than live.
            var sway = Math.Sin(elapsed.TotalSeconds / 11.0 + i * 1.7) * 3_500;
            var swayBearing = sway < 0 ? (toOwnRear + 90) % 360 : (toOwnRear + 270) % 360;
            point = GeoMath.Destination(point.Lat, point.Lon, swayBearing, Math.Abs(sway));

            var branch = GroundBranches[(i * 3 + (eastern ? 1 : 0)) % GroundBranches.Length];
            var symbol = SymbolCode(eastern, 'G', branch.Function);

            // A formation that has just been destroyed is reported once with an expiry in the past,
            // which is what makes the object disappear from the situation, and then simply isn't
            // reported again until the next replacement arrives.
            if (i == lost)
            {
                if (!JustLost(settings, elapsed)) continue;

                var killed = Track(settings, now, $"{prefix}:{i:D2}", names[i % names.Length], point, null, 0, 0,
                    symbol, $"{branch.Role}, destroyed in the {EasternFlankGeometry.Front[index].Sector} sector");
                killed.Symbol.ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now.AddSeconds(-1)) };

                yield return killed;

                // The loss report is only interesting while it is news, so it expires with the
                // window rather than lingering in the event list for the rest of the demo.
                var report = Event(settings, $"flank:event:loss:{(eastern ? "east" : "west")}:{i:D2}",
                    $"{names[i % names.Length]} combat ineffective",
                    $"{branch.Role} destroyed in the {EasternFlankGeometry.Front[index].Sector} sector",
                    eastern ? 3 : 4, point, reporter, now, nowTs);
                report.ActionEvent.ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now.AddSeconds(LossWindowSeconds(settings))) };

                yield return report;
                continue;
            }

            // Units near the active breakthrough are moving, the rest are holding.
            var center = eastern
                ? EasternFlankGeometry.EasternBreakthroughIndex
                : EasternFlankGeometry.WesternCounterIndex;
            var engaged = push * Falloff(index, center);

            var posture = engaged > 0.4 ? "attacking" : "holding";
            yield return Track(settings, now, $"{prefix}:{i:D2}", names[i % names.Length], point, null,
                (toOwnRear + 180) % 360, 2 + engaged * 8, symbol,
                $"{branch.Role}, {EasternFlankGeometry.Front[index].Sector} sector, {posture}");
        }
    }

    /// <summary>
    ///     Transports working their way along a strategic route. Vessels while the leg is at sea,
    ///     ground transport once ashore - which is what makes the transatlantic flow read as a sea
    ///     movement that turns into a road movement.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> Reinforcements(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed, RouteNode[] route, string prefix, string callsignPrefix, string seaSymbol,
        string landSymbol, int lastSeaLeg, int count, string description)
    {
        var path = route.Select(n => (n.Lat, n.Lon)).ToArray();

        // One trip takes a full cycle, so a demo shows reinforcements arriving as the front swings.
        var trip = Fraction(elapsed, settings.CycleDuration);

        for (var i = 0; i < count; i++)
        {
            var own = Wrap(trip - (double)i / Math.Max(1, count));
            var (point, bearing, leg) = PointOnPath(path, own);
            var atSea = leg < lastSeaLeg;

            yield return Track(settings, now, $"{prefix}:{i:D2}", $"{callsignPrefix} {i + 1:D2}", point,
                atSea ? null : 200, bearing, PathSpeed(path, settings.CycleDuration),
                atSea ? seaSymbol : landSymbol, $"{description} ({route[Math.Min(leg + 1, route.Length - 1)].Name})");
        }
    }

    /// <summary>Air patrols orbiting behind each side's own front.</summary>
    private static IEnumerable<UpdateSituationObject> AirPatrols(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed, (double Lat, double Lon)[] front)
    {
        // Tied to the cycle rather than fixed, so shortening the cycle for a demo speeds the whole
        // picture up instead of leaving the aircraft crawling.
        var orbit = Fraction(elapsed, settings.CycleDuration * 0.3) * 360;

        for (var side = 0; side < 2; side++)
        {
            var eastern = side == 1;
            for (var i = 0; i < settings.AirPatrolsPerSide; i++)
            {
                var alongFront = (i + 0.5) / settings.AirPatrolsPerSide;
                var (onLine, index) = AlongFront(front, alongFront);
                var westward = EasternFlankGeometry.WestwardBearing(index);
                var toOwnRear = eastern ? (westward + 180) % 360 : westward;
                var station = GeoMath.Destination(onLine.Lat, onLine.Lon, toOwnRear, 180_000);

                var angle = (orbit + i * 120) % 360;
                var point = GeoMath.Destination(station.Lat, station.Lon, angle, 70_000);

                // A different type per station: fighters, early warning, tanker, attack helicopters,
                // unmanned reconnaissance, bombers.
                var type = AirTypes[(i + (eastern ? 3 : 0)) % AirTypes.Length];

                yield return Track(settings, now, $"flank:air:{(eastern ? "east" : "west")}:{i:D2}",
                    $"{type.Callsign} {(eastern ? 30 : 10) + i}", point, type.Altitude, (angle + 90) % 360,
                    type.Speed, SymbolCode(eastern, 'A', type.Function),
                    $"{type.Role}, {(eastern ? "eastern side" : "NATO side")}");
            }
        }
    }

    /// <summary>
    ///     The vessels at sea, out in the oceans rather than on the flank: the North Atlantic, the
    ///     Barents and the North Pacific carry a two-hull group each, everything else is a single
    ///     ship working its own patch of the Pacific, the South Atlantic, the Indian Ocean or the
    ///     Arabian Sea - a long way from the front, which is where most of a navy actually is.
    ///     Each vessel circles its station; the two of a pair are kept apart on the same circle so
    ///     they read as a formation rather than as one icon on top of another.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> NavalGroups(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed)
    {
        var orbit = Fraction(elapsed, settings.CycleDuration * 0.8) * 360;

        for (var s = 0; s < EasternFlankGeometry.NavalStations.Length; s++)
        {
            var station = EasternFlankGeometry.NavalStations[s];
            var eastern = station.Side == Allegiance.Eastern;
            var onStation = Math.Min(settings.MaxVesselsPerNavalGroup, station.Vessels.Length);

            for (var i = 0; i < onStation; i++)
            {
                var vessel = station.Vessels[i];
                var submarine = vessel.Dimension == 'U';

                // A lone ship runs the middle of its station; a pair is split across the same
                // circle, the second hull half a turn behind the first.
                var angle = (orbit + s * 37 + (onStation > 1 ? i * 150 : 0)) % 360;
                var radius = station.RadiusKm * 1000 * (onStation > 1 && i > 0 ? 0.85 : 0.5);
                var point = GeoMath.Destination(station.Lat, station.Lon, angle, radius);

                // The station name only means something when more than one hull is on it; a single
                // ship is reported under its own name and says where it is in the description.
                var context = onStation > 1 ? $"{station.Name}, {station.Waters}" : station.Waters;

                yield return Track(settings, now, $"flank:sea:{(eastern ? "east" : "west")}:{s:D2}{i:D2}",
                    vessel.Name, point, submarine ? -120 : null,
                    (angle + 90) % 360, submarine ? 6 : 9,
                    SymbolCode(eastern, vessel.Dimension, vessel.Function),
                    $"{vessel.Role}, {context}");
            }
        }
    }

    /// <summary>
    ///     Special forces teams working deep in the other side's hinterland: in along the route, time
    ///     on the objective, then back out - a slower tempo than anything on the front line.
    ///     They carry the bare special operations battle dimension with no function identifier -
    ///     "SFFP-----------" and "SHFP-----------" - because a client that derives its own type from
    ///     the symbol code reads those back as SOF ("a-f-F" and "a-h-F" in a TAK client). Naming the
    ///     branch in the function identifier would push the type a level deeper, to something like
    ///     "a-f-F-G", which is not what such a client draws as special forces; the branch is carried
    ///     in the description instead, where it costs nothing.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> SpecialForces(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed)
    {
        var perSide = new Dictionary<Allegiance, int>();
        var index = 0;

        foreach (var route in EasternFlankGeometry.SpecialForcesRoutes)
        {
            perSide.TryGetValue(route.Side, out var used);
            if (used >= settings.SpecialForcesTeamsPerSide) continue;
            perSide[route.Side] = used + 1;

            var eastern = route.Side == Allegiance.Eastern;
            var infiltration = settings.CycleDuration * 1.5;
            var own = Fraction(elapsed, infiltration);

            // Out and back, with the deepest point of the route spent on the objective.
            var outbound = own < 0.5;
            var alongPath = outbound ? own * 2 : (1 - own) * 2;
            var (point, bearing, _) = PointOnPath(route.Path, alongPath);
            var onObjective = alongPath > 0.9;
            var movement = outbound ? "infiltrating" : "exfiltrating";
            var phase = onObjective ? "on the objective" : movement;

            yield return Track(settings, now, $"flank:sof:{(eastern ? "east" : "west")}:{index:D2}", route.Name,
                point, null, outbound ? bearing : (bearing + 180) % 360,
                onObjective ? 0.5 : PathSpeed(route.Path, infiltration) / 2,
                SymbolCode(eastern, 'F', string.Empty),
                $"Special operations team ({BranchName(route.Branch)}), {phase}. {route.Objective}");

            index++;
        }
    }

    /// <summary>
    ///     Reconnaissance satellites on their ground tracks. Position comes from a plain circular
    ///     orbit projected onto the rotating earth, which is enough to make the passes look right on
    ///     a map; the reported speed is the real orbital figure rather than the compressed demo one.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> Satellites(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed)
    {
        if (!settings.ShowSatellites) yield break;

        var orbits = elapsed.TotalSeconds / settings.SatelliteOrbitDuration.TotalSeconds;

        for (var i = 0; i < EasternFlankGeometry.Satellites.Length; i++)
        {
            var satellite = EasternFlankGeometry.Satellites[i];
            var point = GroundTrack(satellite, orbits);

            // Heading comes from where the track goes next rather than from orbital mechanics.
            var ahead = GroundTrack(satellite, orbits + 0.002);
            var eastern = satellite.Side == Allegiance.Eastern;

            yield return Track(settings, now, $"flank:space:{(eastern ? "east" : "west")}:{i:D2}", satellite.Name,
                point, 500_000, GeoMath.Bearing(point, ahead), 7500,
                eastern ? EasternSatellite : WesternSatellite,
                $"Reconnaissance satellite, {satellite.Task}");
        }
    }

    /// <summary>Unidentified tracks drifting across the line - sensor contacts nobody has classified yet.</summary>
    private static IEnumerable<UpdateSituationObject> UnknownContacts(EasternFlankOptions settings,
        DateTimeOffset now, TimeSpan elapsed)
    {
        var lanes = EasternFlankGeometry.UnknownContacts;

        for (var i = 0; i < Math.Min(settings.UnknownContactCount, lanes.Length); i++)
        {
            var contact = lanes[i];
            var duration = settings.CycleDuration * 0.6;
            var own = Wrap(Fraction(elapsed, duration) + i * 0.25);
            var outbound = own < 0.5;
            var (point, bearing, _) = PointOnPath(contact.Lane, outbound ? own * 2 : (1 - own) * 2);

            yield return Track(settings, now, $"flank:unknown:{i:D2}", contact.Name, point,
                contact.Air ? 7000 : null, outbound ? bearing : (bearing + 180) % 360,
                PathSpeed(contact.Lane, duration), contact.Air ? UnknownAir : UnknownGround,
                contact.Air
                    ? "Unidentified air track, classification pending"
                    : "Unidentified ground track, classification pending");
        }
    }

    /// <summary>Civilian shipping and relief movements: away from the fighting, on the same map.</summary>
    private static IEnumerable<UpdateSituationObject> NeutralTraffic(EasternFlankOptions settings, DateTimeOffset now,
        TimeSpan elapsed)
    {
        var lanes = EasternFlankGeometry.NeutralTraffic;

        for (var i = 0; i < Math.Min(settings.NeutralTrafficCount, lanes.Length); i++)
        {
            var traffic = lanes[i];
            var duration = settings.CycleDuration * 1.2;
            var own = Wrap(Fraction(elapsed, duration) + i * 0.3);
            var outbound = own < 0.5;
            var (point, bearing, _) = PointOnPath(traffic.Lane, outbound ? own * 2 : (1 - own) * 2);

            yield return Track(settings, now, $"flank:neutral:{i:D2}", traffic.Name, point, null,
                outbound ? bearing : (bearing + 180) % 360, PathSpeed(traffic.Lane, duration),
                traffic.Sea ? NeutralSea : NeutralGround, traffic.Kind);
        }
    }

    /// <summary>
    ///     The fixed installations: bases and depots on either side, plus civilian industry and
    ///     transport drawn neutral - they belong to nobody's order of battle but matter to the picture.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> Installations(EasternFlankOptions settings, DateTimeOffset now)
    {
        if (!settings.ShowInstallations) yield break;

        for (var i = 0; i < EasternFlankGeometry.Installations.Length; i++)
        {
            var site = EasternFlankGeometry.Installations[i];
            var affiliation = site.Side switch
            {
                Allegiance.Western => 'F',
                Allegiance.Eastern => 'H',
                _ => 'N'
            };

            yield return Track(settings, now, $"flank:site:{i:D2}", site.Name, (site.Lat, site.Lon), null, 0, 0,
                $"S{affiliation}GP{site.Function.PadRight(11, '-')}", site.Kind);
        }
    }

    /// <summary>
    ///     Incident reports with imagery attached: sabotage, unmanned reconnaissance, an abduction.
    ///     One arrives per window and stays for two, so a viewer sees reports coming in and ageing
    ///     out rather than a static wall of them.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> IncidentReports(EasternFlankOptions settings, Identity reporter,
        DateTimeOffset now, Timestamp nowTs, TimeSpan elapsed)
    {
        if (!settings.ShowIncidents) yield break;

        var incidents = EasternFlankGeometry.Incidents;
        var window = settings.CycleDuration.TotalSeconds / 3;
        var slot = (int)Math.Floor(elapsed.TotalSeconds / window);

        // The current report and the one before it, so there are normally two on the map.
        for (var back = 0; back < 2; back++)
        {
            var incident = incidents[Math.Abs(slot - back) % incidents.Length];
            var expiry = now.AddSeconds(window * (2 - back));

            yield return new UpdateSituationObject
            {
                PictureDocument = new UpdatePictureDocument
                {
                    Identity = new Identity { StringIdentity = $"flank:picture:{incident.Id}" },
                    Reporter = reporter,
                    ReportingTime = nowTs,
                    ExpiryTime = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(expiry) },
                    Name = new UpdatePropertyString { Content = incident.Name },
                    AdditionalInformation = new UpdatePropertyString
                    { Content = $"{incident.Kind}: {incident.Description}" },
                    PictureData = new UpdatePropertyByteArray
                    { Content = ByteString.CopyFrom(TinyPng), Type = "image/png" },
                    LowResPictureData = new UpdatePropertyByteArray
                    { Content = ByteString.CopyFrom(TinyPng), Type = "image/png" },
                    MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Warning },
                    MessagePrecedence = new UpdatePropertyMessagePrecedence
                    { Content = MessagePrecedenceType.Immediate },
                    Location = new UpdatePropertyLocation
                    {
                        Content = new SymbolLocation
                        {
                            Point = new Point
                            {
                                LocationTime = nowTs,
                                GeoPoint = SketchBuilder.Geo((incident.Lat, incident.Lon))
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>
    ///     Which formation that side has just lost, or -1 while it is not losing one. Picked from
    ///     the window number rather than at random, so the same demo loses the same units.
    /// </summary>
    private static int DestroyedIndex(EasternFlankOptions settings, TimeSpan elapsed, bool eastern)
    {
        if (!settings.ShowLosses || settings.FrontUnitsPerSide == 0) return -1;

        var slot = (int)Math.Floor(elapsed.TotalSeconds / LossWindowSeconds(settings));
        if (slot < 1) return -1;

        return Math.Abs(slot * 5 + (eastern ? 3 : 0)) % settings.FrontUnitsPerSide;
    }

    /// <summary>Whether the loss happened just now - the moment the formation is reported gone.</summary>
    private static bool JustLost(EasternFlankOptions settings, TimeSpan elapsed)
    {
        var window = LossWindowSeconds(settings);
        var intoWindow = elapsed.TotalSeconds % window;
        return intoWindow < Math.Max(settings.UpdateInterval.TotalSeconds * 1.5, 3);
    }

    /// <summary>How long one side goes between losses.</summary>
    private static double LossWindowSeconds(EasternFlankOptions settings)
    {
        return settings.CycleDuration.TotalSeconds / 6;
    }

    /// <summary>
    ///     A MIL-STD-2525C symbol code: warfighting scheme, the side's affiliation, a battle
    ///     dimension, present status, and the function identifier padded out to the full 15
    ///     characters. An empty function identifier is a valid code for the dimension as a whole.
    /// </summary>
    private static string SymbolCode(bool eastern, char dimension, string function)
    {
        return $"S{(eastern ? 'H' : 'F')}{dimension}P{function.PadRight(11, '-')}";
    }

    /// <summary>The special operations branch a team belongs to, spelled out for its description.</summary>
    private static string BranchName(string branch)
    {
        return branch switch
        {
            "A" => "aviation",
            "N" => "maritime",
            _ => "ground"
        };
    }

    /// <summary>
    ///     A satellite's sub-point after <paramref name="orbits" /> revolutions: a circular orbit of
    ///     the given inclination, with the earth turning underneath so successive passes shift west.
    /// </summary>
    private static (double Lat, double Lon) GroundTrack(SatelliteOrbit orbit, double orbits)
    {
        var argument = 2 * Math.PI * orbits + orbit.PhaseDeg * Math.PI / 180.0;
        var inclination = orbit.InclinationDeg * Math.PI / 180.0;

        var latitude = Math.Asin(Math.Sin(inclination) * Math.Sin(argument)) * 180.0 / Math.PI;
        var longitudeInOrbit = Math.Atan2(Math.Cos(inclination) * Math.Sin(argument), Math.Cos(argument))
                               * 180.0 / Math.PI;

        // Roughly 22.5 degrees of westward shift per revolution, as a real low orbit has.
        var longitude = orbit.StartLongitude + longitudeInOrbit - 22.5 * orbits;
        return (latitude, ((longitude + 180) % 360 + 360) % 360 - 180);
    }

    // ------------------------------------------------------------------ objects

    /// <summary>
    ///     The planned control measures over the theater, drawn as sketches because a sketch is the
    ///     one place in the contract where free geometry carries its own color and line style.
    ///     Deliberately one of each shape a client has to be able to draw for an area: the named
    ///     areas of interest are polygons of exactly four corners, which is what makes them a
    ///     rectangle rather than a closed freehand shape; the phase lines are polylines; and the
    ///     missile engagement zones are ellipses with unequal axes turned along the line - every
    ///     other ellipse on this map is a circle, and a circle never exercises a client's handling
    ///     of the major axis, the minor axis and the rotation between them.
    ///     These are planned graphics, so unlike the FLOT they stay where they were drawn and the
    ///     front moves through them over the cycle. All of them come off the baseline front, which
    ///     is what keeps each one on the sector it was drawn for.
    /// </summary>
    private static IEnumerable<UpdateSituationObject> ControlMeasures(Identity reporter, Timestamp nowTs)
    {
        // --- Named areas of interest: rectangles -------------------------------
        // A side draws an NAI over ground the other side has to use, so each one sits across the
        // line from the side that is watching it.
        yield return SketchBuilder.Document("flank:box:nai:west", "NAI 1 BREST APPROACHES",
            "Named area of interest: eastern assembly areas feeding the Polish border sector",
            reporter, nowTs,
            SketchBuilder.Element(
                SketchBuilder.Polygon(nowTs, "NAI 1 BREST APPROACHES",
                    Box(EasternFlankGeometry.EasternBreakthroughIndex, -70, 180, 130)),
                WesternColor, AreaOfInterestWidth, LineStyle.Dash));

        yield return SketchBuilder.Document("flank:box:nai:east", "NAI 2 REZEKNE APPROACHES",
            "Named area of interest: western assembly areas behind the Latvian sector",
            reporter, nowTs,
            SketchBuilder.Element(
                SketchBuilder.Polygon(nowTs, "NAI 2 REZEKNE APPROACHES",
                    Box(EasternFlankGeometry.WesternCounterIndex, 70, 170, 120)),
                EasternColor, AreaOfInterestWidth, LineStyle.Dash));

        yield return SketchBuilder.Document("flank:box:roz", "ROZ CASTLE",
            "Restricted operations zone over the western staging area - airspace reserved for the "
            + "reinforcement flow coming off SLOC AMBER",
            reporter, nowTs,
            SketchBuilder.Element(
                SketchBuilder.Polygon(nowTs, "ROZ CASTLE",
                    Box(EasternFlankGeometry.EasternBreakthroughIndex, 230, 240, 160)),
                NeutralColor, AirspaceWidth, LineStyle.Dot));

        // --- Phase lines: polylines -------------------------------------------
        yield return SketchBuilder.Document("flank:line:pl-copper", "PL COPPER",
            "Phase line behind the western front, fallback and report line", reporter, nowTs,
            SketchBuilder.Element(SketchBuilder.Line(nowTs, "PL COPPER", PhaseLine(110, false)),
                WesternColor, PhaseLineWidth, LineStyle.Dash));

        yield return SketchBuilder.Document("flank:line:pl-basalt", "PL BASALT",
            "Phase line behind the eastern front, release line for the second echelon", reporter, nowTs,
            SketchBuilder.Element(SketchBuilder.Line(nowTs, "PL BASALT", PhaseLine(110, true)),
                EasternColor, PhaseLineWidth, LineStyle.Dash));

        // --- Missile engagement zones: rotated ellipses ------------------------
        yield return SketchBuilder.Document("flank:zone:west:mez", "MEZ COPPER",
            "Western long-range surface-to-air engagement zone", reporter, nowTs,
            SketchBuilder.Element(
                SketchBuilder.Ellipse(nowTs, "MEZ COPPER", OffFront(EasternFlankGeometry.WesternCounterIndex, 150),
                    340_000, 95_000, AlongFrontBearing(EasternFlankGeometry.WesternCounterIndex)),
                WesternColor, EngagementZoneWidth, LineStyle.Solid));

        yield return SketchBuilder.Document("flank:zone:east:mez", "MEZ BASALT",
            "Eastern long-range surface-to-air engagement zone", reporter, nowTs,
            SketchBuilder.Element(
                SketchBuilder.Ellipse(nowTs, "MEZ BASALT",
                    OffFront(EasternFlankGeometry.EasternBreakthroughIndex, -150),
                    340_000, 95_000, AlongFrontBearing(EasternFlankGeometry.EasternBreakthroughIndex)),
                EasternColor, EngagementZoneWidth, LineStyle.Solid));
    }

    /// <summary>
    ///     A box on the baseline front: centered <paramref name="westKm" /> west of node
    ///     <paramref name="index" /> (negative for east of it), <paramref name="alongKm" /> long up
    ///     and down the line and <paramref name="acrossKm" /> wide across it. Exactly four corners,
    ///     walked round in order - a polygon of any other count is a closed area rather than a
    ///     rectangle, and a client that tells the two apart tells them apart by that count.
    /// </summary>
    private static (double Lat, double Lon)[] Box(int index, double westKm, double alongKm, double acrossKm)
    {
        var center = OffFront(index, westKm);
        var westward = EasternFlankGeometry.WestwardBearing(index);
        var eastward = (westward + 180) % 360;
        var southward = AlongFrontBearing(index);
        var northward = (southward + 180) % 360;

        var south = GeoMath.Destination(center.Lat, center.Lon, southward, alongKm * 500);
        var north = GeoMath.Destination(center.Lat, center.Lon, northward, alongKm * 500);

        return
        [
            GeoMath.Destination(south.Lat, south.Lon, westward, acrossKm * 500),
            GeoMath.Destination(north.Lat, north.Lon, westward, acrossKm * 500),
            GeoMath.Destination(north.Lat, north.Lon, eastward, acrossKm * 500),
            GeoMath.Destination(south.Lat, south.Lon, eastward, acrossKm * 500)
        ];
    }

    /// <summary>
    ///     A phase line: every baseline front node set back <paramref name="offsetKm" /> into one
    ///     side's own rear, which is where a line of that kind is drawn and where it stays.
    /// </summary>
    private static (double Lat, double Lon)[] PhaseLine(double offsetKm, bool eastern)
    {
        var points = new (double Lat, double Lon)[EasternFlankGeometry.Front.Length];
        for (var i = 0; i < points.Length; i++)
            points[i] = OffFront(i, eastern ? -offsetKm : offsetKm);

        return points;
    }

    /// <summary>
    ///     A point <paramref name="westKm" /> west of baseline front node <paramref name="index" />,
    ///     or east of it when the distance is negative.
    /// </summary>
    private static (double Lat, double Lon) OffFront(int index, double westKm)
    {
        var node = EasternFlankGeometry.Front[index];
        var westward = EasternFlankGeometry.WestwardBearing(index);
        var bearing = westKm >= 0 ? westward : (westward + 180) % 360;

        return GeoMath.Destination(node.Lat, node.Lon, bearing, Math.Abs(westKm) * 1000);
    }

    /// <summary>
    ///     Which way the line runs at node <paramref name="index" /> - southwards, since the nodes
    ///     are ordered north to south. <see cref="EasternFlankGeometry.WestwardBearing" /> is that
    ///     turned a quarter to the west, so this turns it back.
    /// </summary>
    private static double AlongFrontBearing(int index)
    {
        return (EasternFlankGeometry.WestwardBearing(index) + 270) % 360;
    }

    /// <summary>The static zones of one side, each drawn as a circle.</summary>
    private static IEnumerable<UpdateSituationObject> Zones(string prefix,
        (double Lat, double Lon, double RadiusKm, string Name)[] zones, string kind, (int R, int G, int B) color,
        uint width, LineStyle style, Identity reporter, Timestamp nowTs)
    {
        for (var i = 0; i < zones.Length; i++)
        {
            var zone = zones[i];
            yield return SketchBuilder.Document($"{prefix}:{i:D2}", zone.Name, $"{kind}, radius {zone.RadiusKm} km",
                reporter, nowTs,
                SketchBuilder.Element(
                    SketchBuilder.Ellipse(nowTs, zone.Name, (zone.Lat, zone.Lon), zone.RadiusKm * 1000,
                        zone.RadiusKm * 1000, 0), color, width, style));
        }
    }

    /// <summary>A strategic movement route with named waypoints.</summary>
    private static UpdateSituationObject Route(string id, string name, string description, RouteNode[] nodes,
        (int R, int G, int B) color, RouteType type, Identity reporter, Timestamp nowTs)
    {
        var route = new RouteLocation { LocationTime = nowTs, Name = name };
        foreach (var node in nodes)
            route.WayPoints.Add(new WayPoint
            {
                LatitudeCoordinate = node.Lat,
                LongitudeCoordinate = node.Lon,
                WayPointName = node.Name,
                Comment = node.Comment
            });

        return new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                Name = new UpdatePropertyString { Content = name },
                AdditionalInformation = new UpdatePropertyString { Content = description },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { RouteLocation = route } },
                LineColor = new UpdatePropertyColor
                { Content = new Color { Red = color.R, Green = color.G, Blue = color.B, Alpha = 255 } },
                LineWidth = new UpdatePropertyInt { Content = 3 },
                LineStyle = new UpdatePropertyLineStyle { Content = LineStyle.Dash },
                RouteType = new UpdatePropertyRouteType { Content = type }
            }
        };
    }

    /// <summary>The standing task, whose completion tracks how far the current push has gone.</summary>
    private UpdateSituationObject FrontTask(EasternFlankOptions settings, Identity reporter, Timestamp nowTs,
        double phase, double easternPush, double westernPush)
    {
        var attacking = easternPush > westernPush;
        var push = Math.Max(easternPush, westernPush);

        var task = new UpdateActionTask
        {
            Identity = new Identity { StringIdentity = "flank:task:restore" },
            Reporter = reporter,
            ReportingTime = nowTs,
            Name = new UpdatePropertyString
            { Content = attacking ? "Contain the breakthrough" : "Restore the international border" },
            AdditionalInformation = new UpdatePropertyString
            {
                Content = attacking
                    ? "Eastern forces are attacking through the Polish border sector; contain and block"
                    : "Western counter-offensive is pushing east of the border in the Latvian sector"
            },
            ActionTaskType = new UpdatePropertyActionTask
            { Content = attacking ? ActionTaskType.Engage : ActionTaskType.Attack },
            ActionTaskStatus = new UpdatePropertyActionTaskStatus { Content = ActionTaskStatusType.InProgress },
            ActionTaskPriority = new UpdatePropertyActionTaskPriorityCode { Content = ActionTaskPriorityType.Priority1 },
            CompletionRatio = new UpdatePropertyInt { Content = (int)(push * 100) },
            PlannedStartTime = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(_epoch) },
            PlannedEndTime = new UpdatePropertyTimestamp
            { Content = Timestamp.FromDateTimeOffset(_epoch + settings.CycleDuration) },
            Location = new UpdatePropertyLocation
            {
                Content = new SymbolLocation
                {
                    Point = new Point
                    {
                        LocationTime = nowTs,
                        GeoPoint = SketchBuilder.Geo(FrontPoint(attacking
                            ? EasternFlankGeometry.EasternBreakthroughIndex
                            : EasternFlankGeometry.WesternCounterIndex))
                    }
                }
            }
        };

        task.ActionTaskResources = new UpdatePropertyReferences();
        task.ActionTaskResources.Contents.Add(new Identity
        { StringIdentity = attacking ? "flank:unit:west:corps" : "flank:unit:east:group" });

        // Phase is carried in the description so a viewer can see where in the swing we are.
        task.AdditionalInformation = new UpdatePropertyString
        {
            Content = $"{task.AdditionalInformation.Content} (cycle at {(int)(phase * 100)}%)"
        };

        return new UpdateSituationObject { ActionTask = task };
    }

    /// <summary>A SITREP whose text follows the phase of the swing.</summary>
    private static UpdateSituationObject Sitrep(Identity reporter, Timestamp nowTs, DateTimeOffset now, double phase,
        double easternPush, double westernPush)
    {
        var text = phase switch
        {
            < 0.25 =>
                "Eastern forces have crossed the border in the Polish sector, armored spearheads advancing west, "
                + "defending brigades falling back to prepared positions",
            < 0.5 =>
                "Reinforcements from SLOC AMBER are in contact, the breakthrough has been contained and is being "
                + "pushed back towards the border",
            < 0.75 =>
                "Western counter-offensive under way in the Latvian sector, leading formations are east of the "
                + "border, eastern forces withdrawing",
            _ =>
                "Eastern reserves arriving over LOC GRANITE have stabilised the line, the counter-offensive is "
                + "being pressed back to the border"
        };

        var strength = $"Current push: east {(int)(easternPush * 100)}%, west {(int)(westernPush * 100)}%";

        return new UpdateSituationObject
        {
            TextDocument = new UpdateTextDocument
            {
                Identity = new Identity { StringIdentity = "flank:text:sitrep" },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(now.AddHours(1)) },
                Name = new UpdatePropertyString { Content = "Theater SITREP" },
                PlainContent = new UpdatePropertyString { Content = $"{text}. {strength}" },
                Content = new UpdatePropertyString { Content = $"{text}. {strength}" },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Immediate }
            }
        };
    }

    /// <summary>An event raised at the peak of a push.</summary>
    private static UpdateSituationObject Event(EasternFlankOptions settings, string id, string name,
        string description, int threat, (double Lat, double Lon) point, Identity reporter, DateTimeOffset now,
        Timestamp nowTs)
    {
        return new UpdateSituationObject
        {
            ActionEvent = new UpdateActionEvent
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = nowTs,
                ExpiryTime = new UpdatePropertyTimestamp
                { Content = Timestamp.FromDateTimeOffset(now + settings.TrackTimeToLive) },
                Name = new UpdatePropertyString { Content = name },
                ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.Ambush },
                ThreatLevel = new UpdatePropertyInt { Content = threat },
                DetectionDescription = new UpdatePropertyString { Content = description },
                Location = new UpdatePropertyLocation
                {
                    Content = new SymbolLocation
                    {
                        Point = new Point { LocationTime = nowTs, GeoPoint = SketchBuilder.Geo(point) }
                    }
                }
            }
        };
    }

    /// <summary>One element of either side's ORBAT.</summary>
    private static UpdateSituationObject Unit(string id, string name, UnitDesignation designation,
        string higherFormation, string[] subordinates, (int R, int G, int B) color, string symbolCode,
        Identity reporter, Timestamp nowTs)
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
            { Content = new Color { Red = color.R, Green = color.G, Blue = color.B, Alpha = 255 } },
            SymbolIdentifier = new UpdatePropertySymbolIdentifier
            {
                Content = new SymbolIdentifier
                { SymbolCatalog = SymbolCatalog.Mil2525C, StringIdentifier = symbolCode }
            },
            SubordinatedOrganizationUnitCollection = new UpdatePropertyReferences()
        };

        foreach (var subordinate in subordinates)
            unit.SubordinatedOrganizationUnitCollection.Contents.Add(new Identity { StringIdentity = subordinate });

        return new UpdateSituationObject { OrganizationUnit = unit };
    }

    /// <summary>A moving symbol, as the shared track mapping builds it.</summary>
    private static UpdateSituationObject Track(EasternFlankOptions settings, DateTimeOffset now, string id,
        string name, (double Lat, double Lon) point, double? altitudeM, double courseDeg, double speedMs,
        string symbolCode, string description)
    {
        return TrackUpdateFactory.CreateSymbolUpdate(
            new TrackReport(id, name, point.Lat, point.Lon, altitudeM, courseDeg, speedMs, description),
            settings.ReporterId, symbolCode, SymbolCatalog.Mil2525C, now, settings.TrackTimeToLive);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The baseline position of one front node.</summary>
    private static (double Lat, double Lon) FrontPoint(int index)
    {
        var node = EasternFlankGeometry.Front[index];
        return (node.Lat, node.Lon);
    }

    /// <summary>A point on the current front trace, plus the node index it is closest to.</summary>
    private static ((double Lat, double Lon) Point, int Index) AlongFront((double Lat, double Lon)[] front,
        double fraction)
    {
        var scaled = Math.Clamp(fraction, 0, 1) * (front.Length - 1);
        var index = (int)Math.Floor(scaled);
        if (index >= front.Length - 1) return (front[^1], front.Length - 1);

        var onLeg = scaled - index;
        return (EasternFlankGeometry.Between(front[index], front[index + 1], onLeg),
            onLeg < 0.5 ? index : index + 1);
    }

    /// <summary>Where in the current repetition of <paramref name="period" /> the elapsed time sits, as [0,1).</summary>
    private static double Fraction(TimeSpan elapsed, TimeSpan period)
    {
        return Wrap(elapsed.TotalSeconds / period.TotalSeconds);
    }

    /// <summary>Wraps any value into [0,1), so followers offset behind a leader stay on the path.</summary>
    private static double Wrap(double value)
    {
        var wrapped = value % 1.0;
        return wrapped < 0 ? wrapped + 1 : wrapped;
    }

    /// <summary>
    ///     Position, heading and leg index at <paramref name="fraction" /> of the way along a path,
    ///     measured by distance so long legs take proportionally longer.
    /// </summary>
    private static ((double Lat, double Lon) Point, double BearingDeg, int Leg) PointOnPath(
        (double Lat, double Lon)[] path, double fraction)
    {
        var legs = new double[path.Length - 1];
        var total = 0.0;
        for (var i = 0; i < legs.Length; i++)
        {
            legs[i] = GeoMath.DistanceMeters(path[i].Lat, path[i].Lon, path[i + 1].Lat, path[i + 1].Lon);
            total += legs[i];
        }

        var target = Math.Clamp(fraction, 0, 1) * total;
        for (var i = 0; i < legs.Length; i++)
        {
            if (target > legs[i] && i < legs.Length - 1)
            {
                target -= legs[i];
                continue;
            }

            var onLeg = legs[i] <= 0 ? 0 : target / legs[i];
            return (EasternFlankGeometry.Between(path[i], path[i + 1], onLeg),
                GeoMath.Bearing(path[i], path[i + 1]), i);
        }

        return (path[^1], 0, legs.Length - 1);
    }

    /// <summary>Average speed needed to walk the whole path within <paramref name="duration" />.</summary>
    private static double PathSpeed((double Lat, double Lon)[] path, TimeSpan duration)
    {
        var total = 0.0;
        for (var i = 0; i < path.Length - 1; i++)
            total += GeoMath.DistanceMeters(path[i].Lat, path[i].Lon, path[i + 1].Lat, path[i + 1].Lon);

        return total / duration.TotalSeconds;
    }
}
