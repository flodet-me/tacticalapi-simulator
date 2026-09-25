using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="GeometryShowcaseSource" />
///     (src/adapter/TacticalApi.Simulator.Sources.Synthetic/GeometryShowcaseSource.cs).
/// </summary>
public sealed class GeometryShowcaseSourceTests
{
    private const double EarthRadiusM = 6371000.0;

    [Fact]
    public void Source_ExposesNameAndIntervalFromOptions()
    {
        // Arrange
        var options = new GeometryShowcaseOptions { UpdateInterval = TimeSpan.FromSeconds(3) };
        var source = new GeometryShowcaseSource(TestHelpers.Options(options), TimeProvider.System,
            NullLogger<GeometryShowcaseSource>.Instance);

        // Act & Assert
        Assert.Equal("GeometryShowcase", source.Name);
        Assert.Equal(TimeSpan.FromSeconds(3), source.Interval);
    }

    [Fact]
    public async Task ProduceAsync_EmitsEveryLocationKindOnce()
    {
        // Arrange
        var source = new GeometryShowcaseSource(TestHelpers.Options(new GeometryShowcaseOptions()),
            TimeProvider.System, NullLogger<GeometryShowcaseSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SymbolLocation.LocationOneofCase.Line, ElementLocation(updates, "showcase:line", 0).LocationCase);
        Assert.Equal(SymbolLocation.LocationOneofCase.Polygon,
            ElementLocation(updates, "showcase:rectangle", 0).LocationCase);
        Assert.Equal(SymbolLocation.LocationOneofCase.Ellipse,
            ElementLocation(updates, "showcase:circle", 0).LocationCase);
        Assert.Equal(SymbolLocation.LocationOneofCase.Ellipse,
            ElementLocation(updates, "showcase:ellipse", 0).LocationCase);

        // The multi-element sketch carries all three kinds at once.
        Assert.Equal(SymbolLocation.LocationOneofCase.Line, ElementLocation(updates, "showcase:multi", 0).LocationCase);
        Assert.Equal(SymbolLocation.LocationOneofCase.Polygon,
            ElementLocation(updates, "showcase:multi", 1).LocationCase);
        Assert.Equal(SymbolLocation.LocationOneofCase.Ellipse,
            ElementLocation(updates, "showcase:multi", 2).LocationCase);

        var symbolOnLine = updates.Single(u => u.Symbol?.Identity.StringIdentity == "showcase:symbol:line");
        Assert.Equal(SymbolLocation.LocationOneofCase.Line, symbolOnLine.Symbol.Location.Content.LocationCase);

        var center = updates.Single(u => u.Symbol?.Identity.StringIdentity == "showcase:symbol:center");
        Assert.Equal(SymbolLocation.LocationOneofCase.Point, center.Symbol.Location.Content.LocationCase);
    }

    [Fact]
    public async Task ProduceAsync_RectangleHasFourCornersAndLineHasThreePoints()
    {
        // Arrange
        var source = new GeometryShowcaseSource(TestHelpers.Options(new GeometryShowcaseOptions()),
            TimeProvider.System, NullLogger<GeometryShowcaseSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, ElementLocation(updates, "showcase:rectangle", 0).Polygon.Points.Count);
        Assert.Equal(3, ElementLocation(updates, "showcase:line", 0).Line.Points.Count);
    }

    [Fact]
    public async Task ProduceAsync_EllipseAxesMatchConfiguredSizeAndAreOrthogonal()
    {
        // Arrange
        var options = new GeometryShowcaseOptions { ShapeSizeM = 800 };
        var source = new GeometryShowcaseSource(TestHelpers.Options(options), TimeProvider.System,
            NullLogger<GeometryShowcaseSource>.Instance);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var ellipse = ElementLocation(updates, "showcase:ellipse", 0).Ellipse;
        var circle = ElementLocation(updates, "showcase:circle", 0).Ellipse;

        // Assert
        Assert.Equal(400, DistanceM(ellipse.CenterPoint, ellipse.FirstConjugateDiameterPoint), 1.0);
        Assert.Equal(160, DistanceM(ellipse.CenterPoint, ellipse.SecondConjugateDiameterPoint), 1.0);
        Assert.Equal(90, Math.Abs(Bearing(ellipse.CenterPoint, ellipse.FirstConjugateDiameterPoint) -
                                  Bearing(ellipse.CenterPoint, ellipse.SecondConjugateDiameterPoint)), 0.5);

        // A circle is the same message with both axes equal.
        Assert.Equal(DistanceM(circle.CenterPoint, circle.FirstConjugateDiameterPoint),
            DistanceM(circle.CenterPoint, circle.SecondConjugateDiameterPoint), 1.0);
    }

    [Fact]
    public async Task ProduceAsync_IdentitiesAreStableAcrossCycles()
    {
        // Arrange
        var source = new GeometryShowcaseSource(TestHelpers.Options(new GeometryShowcaseOptions()),
            TimeProvider.System, NullLogger<GeometryShowcaseSource>.Instance);

        // Act
        var first = await source.ProduceAsync(CancellationToken.None);
        var second = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(first.Select(Identity), second.Select(Identity));
    }

    private static string Identity(UpdateSituationObject update)
    {
        return update.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol
            ? update.Symbol.Identity.StringIdentity
            : update.SketchDocument.Identity.StringIdentity;
    }

    private static SymbolLocation ElementLocation(IReadOnlyList<UpdateSituationObject> updates, string id, int index)
    {
        var sketch = updates.Single(u => u.SketchDocument?.Identity.StringIdentity == id).SketchDocument;
        return sketch.Location.Content.SketchLocation.Elements[index].Location;
    }

    private static double DistanceM(GeoPoint from, GeoPoint to)
    {
        var phi1 = from.LatitudeCoordinate * Math.PI / 180.0;
        var phi2 = to.LatitudeCoordinate * Math.PI / 180.0;
        var deltaPhi = (to.LatitudeCoordinate - from.LatitudeCoordinate) * Math.PI / 180.0;
        var deltaLambda = (to.LongitudeCoordinate - from.LongitudeCoordinate) * Math.PI / 180.0;

        var h = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Sqrt(h));
    }

    private static double Bearing(GeoPoint from, GeoPoint to)
    {
        var phi1 = from.LatitudeCoordinate * Math.PI / 180.0;
        var phi2 = to.LatitudeCoordinate * Math.PI / 180.0;
        var deltaLambda = (to.LongitudeCoordinate - from.LongitudeCoordinate) * Math.PI / 180.0;

        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
        return (Math.Atan2(y, x) * 180.0 / Math.PI + 360) % 360;
    }
}
