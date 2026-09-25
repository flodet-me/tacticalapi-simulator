using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="EasternFlankSource" />
///     (src/adapter/TacticalApi.Simulator.Sources.Synthetic/EasternFlankSource.cs).
/// </summary>
public sealed class EasternFlankSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Source_ExposesNameAndIntervalFromOptions()
    {
        // Arrange
        var options = new EasternFlankOptions { UpdateInterval = TimeSpan.FromSeconds(3) };
        var source = new EasternFlankSource(TestHelpers.Options(options), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act & Assert
        Assert.Equal("EasternFlank", source.Name);
        Assert.Equal(TimeSpan.FromSeconds(3), source.Interval);
    }

    [Fact]
    public async Task ProduceAsync_EmitsBothSidesAndTheStrategicFlows()
    {
        // Arrange
        var options = new EasternFlankOptions
        {
            FrontUnitsPerSide = 10,
            UsReinforcementCount = 4,
            EasternReserveCount = 3,
            AirPatrolsPerSide = 2,
            MaxVesselsPerNavalGroup = 2
        };
        var source = new EasternFlankSource(TestHelpers.Options(options), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var ids = updates.Select(Identity).ToList();

        // Assert
        Assert.Equal(10, ids.Count(id => id.StartsWith("flank:force:west:", StringComparison.Ordinal)));
        Assert.Equal(10, ids.Count(id => id.StartsWith("flank:force:east:", StringComparison.Ordinal)));
        Assert.Equal(4, ids.Count(id => id.StartsWith("flank:reinf:west:", StringComparison.Ordinal)));
        Assert.Equal(3, ids.Count(id => id.StartsWith("flank:reinf:east:", StringComparison.Ordinal)));
        Assert.Equal(4, ids.Count(id => id.StartsWith("flank:air:", StringComparison.Ordinal)));

        // Seventeen naval stations spread across the oceans, three of them holding two hulls.
        Assert.Equal(20, ids.Count(id => id.StartsWith("flank:sea:", StringComparison.Ordinal)));
        Assert.Equal(5, ids.Count(id => id.StartsWith("flank:reinf:south:", StringComparison.Ordinal)));
        Assert.Contains("flank:route:southern", ids);
        Assert.Contains("flank:line:flot", ids);
        Assert.Contains("flank:line:border", ids);
        Assert.Contains("flank:route:atlantic", ids);
        Assert.Contains("flank:route:eastern", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // Both sides are present as symbols: "SF..." is friendly, "SH..." hostile.
        var codes = updates
            .Where(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol)
            .Select(u => u.Symbol.SymbolIdentifier.Content.StringIdentifier)
            .ToList();
        Assert.Contains(codes, c => c.StartsWith("SF", StringComparison.Ordinal));
        Assert.Contains(codes, c => c.StartsWith("SH", StringComparison.Ordinal));
        Assert.All(codes, c => Assert.Equal(15, c.Length));
    }

    [Fact]
    public async Task ProduceAsync_BulgesWestThenIsPushedBackThenBulgesEast()
    {
        // Arrange
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { CycleDuration = TimeSpan.FromMinutes(20), MaxBulgeKm = 160 };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act: sample the four quarters of one full swing.
        var atStart = await FrontAsync(source);
        time.Advance(TimeSpan.FromMinutes(5));
        var easternPeak = await FrontAsync(source);
        time.Advance(TimeSpan.FromMinutes(5));
        var backAtBorder = await FrontAsync(source);
        time.Advance(TimeSpan.FromMinutes(5));
        var westernPeak = await FrontAsync(source);

        var node = EasternFlankGeometryNode(EasternFlankBreakthroughIndex);
        var counterNode = EasternFlankGeometryNode(EasternFlankCounterIndex);

        // Assert
        Assert.True(atStart[EasternFlankBreakthroughIndex].LongitudeCoordinate - node.Lon < 0.01);

        // At the eastern peak the line has moved west (smaller longitude) into the other side.
        Assert.True(easternPeak[EasternFlankBreakthroughIndex].LongitudeCoordinate < node.Lon - 1.0,
            "eastern breakthrough should push the line west");

        // Half a cycle later it is back on the border, give or take the idle ripple that keeps
        // quiet sectors from looking like a drawn boundary.
        Assert.True(Math.Abs(backAtBorder[EasternFlankBreakthroughIndex].LongitudeCoordinate - node.Lon) < 0.4,
            "the line should be back at the border between the two pushes");

        // At the western peak the line has moved east at the other sector.
        Assert.True(westernPeak[EasternFlankCounterIndex].LongitudeCoordinate > counterNode.Lon + 1.0,
            "western counter-offensive should push the line east");
    }

    [Fact]
    public async Task ProduceAsync_ShowsCapturedGroundOnlyWhileASideIsAhead()
    {
        // Arrange
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { CycleDuration = TimeSpan.FromMinutes(20) };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var atBorder = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        var easternPeak = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10));
        var westernPeak = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain("flank:area:east-gain", atBorder.Select(Identity));
        Assert.Contains("flank:area:east-gain", easternPeak.Select(Identity));
        Assert.DoesNotContain("flank:area:west-gain", easternPeak.Select(Identity));
        Assert.Contains("flank:area:west-gain", westernPeak.Select(Identity));
    }

    [Fact]
    public async Task ProduceAsync_MovesReinforcementsAlongTheirRoutes()
    {
        // Arrange
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { CycleDuration = TimeSpan.FromMinutes(20), UsReinforcementCount = 2 };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var before = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(4));
        var after = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var first = Point(before, "flank:reinf:west:00");
        var second = Point(after, "flank:reinf:west:00");
        Assert.True(GreatCircleKm(first, second) > 100, "a transport should have covered ground in four minutes");
    }

    [Fact]
    public async Task ProduceAsync_EmitsSatellitesSpecialForcesAndInstallations()
    {
        // Arrange
        var options = new EasternFlankOptions { SpecialForcesTeamsPerSide = 3 };
        var source = new EasternFlankSource(TestHelpers.Options(options), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var ids = updates.Select(Identity).ToList();

        // Assert
        Assert.Equal(3, ids.Count(id => id.StartsWith("flank:space:west:", StringComparison.Ordinal)));
        Assert.Equal(2, ids.Count(id => id.StartsWith("flank:space:east:", StringComparison.Ordinal)));
        Assert.Equal(3, ids.Count(id => id.StartsWith("flank:sof:west:", StringComparison.Ordinal)));
        Assert.Equal(3, ids.Count(id => id.StartsWith("flank:sof:east:", StringComparison.Ordinal)));
        Assert.Equal(33, ids.Count(id => id.StartsWith("flank:site:", StringComparison.Ordinal)));
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // A satellite reports its altitude, which is what puts it above the rest of the picture.
        var satellite = updates.Single(u => Identity(u) == "flank:space:west:00");
        Assert.Equal(500_000, satellite.Symbol.Location.Content.Point.GeoPoint.VerticalDistance);
    }

    [Fact]
    public async Task ProduceAsync_DrawsSpecialForcesWithTheBareSofDimension()
    {
        // Arrange
        var source = new EasternFlankSource(TestHelpers.Options(new EasternFlankOptions()), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var teams = updates
            .Where(u => Identity(u).StartsWith("flank:sof:", StringComparison.Ordinal))
            .ToList();

        // Assert: battle dimension "F" and no function identifier at all, which is what a client
        // deriving its own type from the symbol code reads back as special operations forces - a
        // TAK client's "a-f-F" and "a-h-F" - rather than as something a level deeper.
        Assert.NotEmpty(teams);
        foreach (var team in teams)
        {
            var eastern = Identity(team).StartsWith("flank:sof:east:", StringComparison.Ordinal);
            Assert.Equal(eastern ? "SHFP-----------" : "SFFP-----------",
                team.Symbol.SymbolIdentifier.Content.StringIdentifier);
        }
    }

    [Fact]
    public void SpecialForcesRoutes_WorkObjectivesDeepInTheOtherSidesHinterland()
    {
        // Arrange: four routes per side, each ending on the objective the team went in for.
        var routes = EasternFlankGeometry.SpecialForcesRoutes;

        // Assert
        Assert.Equal(4, routes.Count(r => r.Side == Allegiance.Western));
        Assert.Equal(4, routes.Count(r => r.Side == Allegiance.Eastern));

        foreach (var route in routes)
        {
            var objective = route.Path[^1];
            var depthKm = EasternFlankGeometry.Front
                .Min(node => GeoMath.DistanceMeters(node.Lat, node.Lon, objective.Lat, objective.Lon)) / 1000;

            // Measured against the baseline front rather than the current trace, which moves: even
            // at the deepest bulge the line is MaxBulgeKm from the baseline, well inside this.
            Assert.True(depthKm > 500,
                $"{route.Name} works an objective {depthKm:F0} km from the front, which is not the hinterland");
        }
    }

    [Fact]
    public async Task ProduceAsync_DrawsSketchesOfEveryAreaShape()
    {
        // Arrange
        var source = new EasternFlankSource(TestHelpers.Options(new EasternFlankOptions()), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var elements = updates
            .Where(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.SketchDocument)
            .SelectMany(u => u.SketchDocument.Location.Content.SketchLocation.Elements
                .Select(element => (Id: Identity(u), Element: element)))
            .ToList();

        // Assert: a client gets all three area shapes out of one picture.
        Assert.Contains(elements, e => e.Element.Location.LocationCase == SymbolLocation.LocationOneofCase.Line);
        Assert.Contains(elements, e => e.Element.Location.LocationCase == SymbolLocation.LocationOneofCase.Polygon);
        Assert.Contains(elements, e => e.Element.Location.LocationCase == SymbolLocation.LocationOneofCase.Ellipse);

        // The areas of interest and the restricted zone are rectangles, which a client tells from a
        // closed freehand area by the corner count being exactly four.
        foreach (var id in new[] { "flank:box:nai:west", "flank:box:nai:east", "flank:box:roz" })
            Assert.Equal(4, elements.Single(e => e.Id == id).Element.Location.Polygon.Points.Count);

        // The engagement zones are real ellipses rather than circles, so there is a major axis, a
        // minor axis and a rotation between them to handle.
        foreach (var id in new[] { "flank:zone:west:mez", "flank:zone:east:mez" })
        {
            var ellipse = elements.Single(e => e.Id == id).Element.Location.Ellipse;
            var major = GreatCircleKm(ellipse.CenterPoint, ellipse.FirstConjugateDiameterPoint);
            var minor = GreatCircleKm(ellipse.CenterPoint, ellipse.SecondConjugateDiameterPoint);
            Assert.True(major > minor * 2, $"{id} is a circle ({major:F0} by {minor:F0} km), not an ellipse");
        }

        // The phase lines are polylines running the length of the theater.
        foreach (var id in new[] { "flank:line:pl-copper", "flank:line:pl-basalt" })
            Assert.Equal(EasternFlankGeometry.Front.Length,
                elements.Single(e => e.Id == id).Element.Location.Line.Points.Count);
    }

    [Fact]
    public async Task ProduceAsync_DrawsGeometryInSeveralWidths()
    {
        // Arrange
        var source = new EasternFlankSource(TestHelpers.Options(new EasternFlankOptions()), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var elements = updates
            .Where(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.SketchDocument)
            .SelectMany(u => u.SketchDocument.Location.Content.SketchLocation.Elements)
            .ToList();

        var widths = elements.Select(e => e.LineWidth).Distinct().ToList();

        // Assert: the drawn geometry is graded rather than uniform. A picture in one weight tells a
        // viewer nothing about what is binding and what is reference, and it leaves a client's line
        // width handling completely unexercised.
        Assert.True(widths.Count >= 5, $"only {widths.Count} distinct line widths on the drawn geometry");

        // The TAK adapter clamps anything outside this range, so a width beyond it is silently not
        // what was asked for.
        Assert.All(widths, width => Assert.InRange(width, 1u, 10u));
    }

    [Fact]
    public async Task ProduceAsync_UsesAllFourAffiliations()
    {
        // Arrange
        var source = new EasternFlankSource(TestHelpers.Options(new EasternFlankOptions()), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var affiliations = updates
            .Where(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol)
            .Select(u => u.Symbol.SymbolIdentifier.Content.StringIdentifier[1])
            .Distinct()
            .ToList();

        // Assert: friendly, hostile, neutral and unknown are all on the map.
        Assert.Contains('F', affiliations);
        Assert.Contains('H', affiliations);
        Assert.Contains('N', affiliations);
        Assert.Contains('U', affiliations);
    }

    [Fact]
    public async Task ProduceAsync_KeepsEverythingMovingBetweenCycles()
    {
        // Arrange
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { CycleDuration = TimeSpan.FromMinutes(3) };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act: two reports ten seconds apart, as a viewer would see them.
        var before = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        var after = await source.ProduceAsync(CancellationToken.None);

        // Assert
        foreach (var id in new[]
                 {
                     "flank:space:west:00", "flank:sof:west:00", "flank:unknown:00", "flank:neutral:00",
                     "flank:reinf:west:00"
                 })
            Assert.True(GreatCircleKm(Point(before, id), Point(after, id)) > 0.01, $"{id} should have moved");

        // Front formations move too - taking one that survived both reports, since a formation lost
        // in between is deliberately absent from the second one.
        var survivor = before.Select(Identity)
            .Intersect(after.Select(Identity))
            .First(id => id.StartsWith("flank:force:west:", StringComparison.Ordinal));
        Assert.True(GreatCircleKm(Point(before, survivor), Point(after, survivor)) > 0.01,
            $"{survivor} should have moved");

        // Installations are the exception: they are fixed by definition.
        Assert.Equal(0, GreatCircleKm(Point(before, "flank:site:00"), Point(after, "flank:site:00")), 3);
    }

    [Fact]
    public async Task ProduceAsync_KeepsVesselsMostlyAloneAndSpreadAcrossTheOceans()
    {
        // Arrange
        var source = new EasternFlankSource(TestHelpers.Options(new EasternFlankOptions()), TimeProvider.System,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var vessels = updates
            .Where(u => Identity(u).StartsWith("flank:sea:", StringComparison.Ordinal))
            .ToList();

        // The identity is "flank:sea:<side>:<station><index>", two digits each, so everything but
        // the last two characters identifies the station.
        var perStation = vessels
            .GroupBy(u => Identity(u)[..^2])
            .Select(g => g.Count())
            .ToList();

        // Assert: never more than a pair, and most stations hold a single ship.
        Assert.All(perStation, count => Assert.InRange(count, 1, 2));
        Assert.True(perStation.Count(count => count == 1) > perStation.Count(count => count == 2) * 2,
            "most naval stations should hold a single vessel");

        // Spread well beyond the theater: both hemispheres, and all the way round from the Pacific
        // through the Atlantic to the Indian Ocean.
        var points = vessels.Select(u => u.Symbol.Location.Content.Point.GeoPoint).ToList();
        Assert.Contains(points, p => p.LatitudeCoordinate < -10);
        Assert.Contains(points, p => p.LongitudeCoordinate < -100);
        Assert.Contains(points, p => p.LongitudeCoordinate > 60);
        Assert.True(points.Max(p => p.LatitudeCoordinate) - points.Min(p => p.LatitudeCoordinate) > 90,
            "the fleets should span more than 90 degrees of latitude");
    }

    [Fact]
    public async Task ProduceAsync_StampsAnExpiryOnEveryObjectAndPushesItForwardEachCycle()
    {
        // Arrange
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { TrackTimeToLive = TimeSpan.FromMinutes(5) };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        var before = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        var after = await source.ProduceAsync(CancellationToken.None);

        // Assert: nothing is reported without an expiry, or a client that ages objects out by it
        // drops them off the map while the simulator is still re-reporting them.
        Assert.All(before, update => Assert.NotNull(Expiry(update)));

        // The static frame is the point of this: it never changes, so its expiry moving forward is
        // both what keeps it alive and the only thing that makes the re-report visible at all.
        foreach (var id in new[]
                 {
                     "flank:site:00", "flank:unit:west:corps", "flank:route:atlantic", "flank:line:border",
                     "flank:zone:east:ad:00"
                 })
        {
            var first = Expiry(before.Single(u => Identity(u) == id));
            var second = Expiry(after.Single(u => Identity(u) == id));
            Assert.Equal(Start.AddMinutes(5), first!.ToDateTimeOffset());
            Assert.Equal(TimeSpan.FromSeconds(10), second!.ToDateTimeOffset() - first.ToDateTimeOffset());
        }
    }

    [Fact]
    public async Task ProduceAsync_LeavesObjectsThatManageTheirOwnLifetimeAlone()
    {
        // Arrange: a one-minute cycle, so the first loss window (CycleDuration / 6) is ten seconds in.
        var time = new StepTimeProvider(Start);
        var options = new EasternFlankOptions { CycleDuration = TimeSpan.FromMinutes(1) };
        var source = new EasternFlankSource(TestHelpers.Options(options), time,
            NullLogger<EasternFlankSource>.Instance);

        // Act
        await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert: a destroyed formation still carries an expiry in the past, which is what deletes
        // it from the situation - the blanket stamp must not have pushed it into the future.
        var destroyed = updates
            .Where(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol)
            .Where(u => u.Symbol.AdditionalInformation.Content.Contains("destroyed", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(destroyed);
        Assert.All(destroyed, u => Assert.True(u.Symbol.ExpiryTime.Content.ToDateTimeOffset() < time.GetUtcNow()));

        // The SITREP keeps its own hour-long window rather than the track time to live.
        var sitrep = updates.Single(u => Identity(u) == "flank:text:sitrep");
        Assert.Equal(time.GetUtcNow().AddHours(1), Expiry(sitrep)!.ToDateTimeOffset());
    }

    private static async Task<List<GeoPoint>> FrontAsync(EasternFlankSource source)
    {
        var updates = await source.ProduceAsync(CancellationToken.None);
        var flot = updates.Single(u => Identity(u) == "flank:line:flot");
        return flot.SketchDocument.Location.Content.SketchLocation.Elements[0].Location.Line.Points.ToList();
    }

    private const int EasternFlankBreakthroughIndex = 7;
    private const int EasternFlankCounterIndex = 3;

    private static (double Lat, double Lon) EasternFlankGeometryNode(int index)
    {
        // Mirrors the baseline in EasternFlankGeometry.Front, which is internal to the source project.
        (double Lat, double Lon)[] baseline =
        [
            (59.38, 28.19), (58.35, 27.50), (57.15, 27.85), (56.10, 28.05), (55.20, 26.60), (54.35, 23.90),
            (53.30, 23.80), (52.30, 23.60), (51.30, 23.90), (50.40, 25.60), (49.20, 27.80), (47.90, 29.80),
            (46.45, 30.75)
        ];
        return baseline[index];
    }

    private static double GreatCircleKm(GeoPoint from, GeoPoint to)
    {
        var phi1 = from.LatitudeCoordinate * Math.PI / 180;
        var phi2 = to.LatitudeCoordinate * Math.PI / 180;
        var deltaPhi = (to.LatitudeCoordinate - from.LatitudeCoordinate) * Math.PI / 180;
        var deltaLambda = (to.LongitudeCoordinate - from.LongitudeCoordinate) * Math.PI / 180;
        var h = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(h));
    }

    private static GeoPoint Point(IReadOnlyList<UpdateSituationObject> updates, string id)
    {
        return updates.Single(u => Identity(u) == id).Symbol.Location.Content.Point.GeoPoint;
    }

    /// <summary>The expiry an update carries, whichever of the object types it is.</summary>
    private static Timestamp? Expiry(UpdateSituationObject update)
    {
        return update.TypeCase switch
        {
            UpdateSituationObject.TypeOneofCase.Symbol => update.Symbol.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.SketchDocument => update.SketchDocument.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.OrganizationUnit => update.OrganizationUnit.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.Route => update.Route.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.ActionTask => update.ActionTask.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.ActionEvent => update.ActionEvent.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.TextDocument => update.TextDocument.ExpiryTime?.Content,
            UpdateSituationObject.TypeOneofCase.PictureDocument => update.PictureDocument.ExpiryTime?.Content,
            _ => null
        };
    }

    private static string Identity(UpdateSituationObject update)
    {
        return update.TypeCase switch
        {
            UpdateSituationObject.TypeOneofCase.Symbol => update.Symbol.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.SketchDocument => update.SketchDocument.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.OrganizationUnit => update.OrganizationUnit.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.Route => update.Route.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.ActionTask => update.ActionTask.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.ActionEvent => update.ActionEvent.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.TextDocument => update.TextDocument.Identity.StringIdentity,
            UpdateSituationObject.TypeOneofCase.PictureDocument => update.PictureDocument.Identity.StringIdentity,
            _ => string.Empty
        };
    }

    /// <summary>A clock the test moves by hand, so the swing can be sampled without waiting for it.</summary>
    private sealed class StepTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
        }
    }
}
