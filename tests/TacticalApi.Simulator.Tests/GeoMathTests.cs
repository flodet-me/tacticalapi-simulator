using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="GeoMath" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/GeoMath.cs).
/// </summary>
public sealed class GeoMathTests
{
    [Fact]
    public void Destination_DueEast_MovesLongitudeNotLatitude()
    {
        // Arrange/Act: roughly one degree of longitude at the equator.
        var (lat, lon) = GeoMath.Destination(0, 0, 90, 111_320);

        // Assert
        Assert.InRange(lat, -0.01, 0.01);
        Assert.InRange(lon, 0.95, 1.05);
    }

    [Fact]
    public void Destination_DueNorth_MovesLatitudeNotLongitude()
    {
        var (lat, lon) = GeoMath.Destination(0, 0, 0, 111_320);

        Assert.InRange(lat, 0.95, 1.05);
        Assert.InRange(lon, -0.01, 0.01);
    }

    [Fact]
    public void DistanceMeters_OneDegreeOfLongitudeAtEquator_MatchesKnownApproximation()
    {
        var distance = GeoMath.DistanceMeters(0, 0, 0, 1);

        Assert.InRange(distance, 110_000, 112_000);
    }

    [Fact]
    public void DistanceMeters_SamePoint_IsZero()
    {
        var distance = GeoMath.DistanceMeters(52.5, 8.5, 52.5, 8.5);

        Assert.Equal(0, distance, 3);
    }

    [Fact]
    public void Contains_PointInsideSquare_ReturnsTrue()
    {
        (double Lat, double Lon)[] square = [(0, 0), (0, 1), (1, 1), (1, 0)];

        Assert.True(GeoMath.Contains(square, 0.5, 0.5));
    }

    [Fact]
    public void Contains_PointOutsideSquare_ReturnsFalse()
    {
        (double Lat, double Lon)[] square = [(0, 0), (0, 1), (1, 1), (1, 0)];

        Assert.False(GeoMath.Contains(square, 5, 5));
    }
}
