using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="TrackUpdateFactory" />
///     (src/TacticalApi.Simulator.Sources/TrackUpdateFactory.cs).
/// </summary>
public sealed class TrackUpdateFactoryTests
{
    [Fact]
    public void CreateSymbolUpdate_MapsAllTrackFields()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var track = new TrackReport(
            "opensky:abc123",
            "DLH123",
            53.05,
            8.79,
            10_000,
            270,
            230,
            "test");

        // Act
        var update = TrackUpdateFactory.CreateSymbolUpdate(
            track, "REPORTER", "SNAPCF---------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(2));

        // Assert
        Assert.Equal(UpdateSituationObject.TypeOneofCase.Symbol, update.TypeCase);
        var symbol = update.Symbol;
        Assert.Equal("opensky:abc123", symbol.Identity.StringIdentity);
        Assert.Equal("REPORTER", symbol.Reporter.StringIdentity);
        Assert.Equal("DLH123", symbol.Name.Content);
        Assert.Equal("SNAPCF---------", symbol.SymbolIdentifier.Content.StringIdentifier);
        Assert.Equal(SymbolCatalog.Mil2525C, symbol.SymbolIdentifier.Content.SymbolCatalog);

        var point = symbol.Location.Content.Point;
        Assert.Equal(53.05, point.GeoPoint.LatitudeCoordinate);
        Assert.Equal(8.79, point.GeoPoint.LongitudeCoordinate);
        Assert.Equal(10_000, point.GeoPoint.VerticalDistance);
        Assert.Equal(270, point.Course);
        Assert.Equal(230, point.Speed);

        Assert.Equal(now.AddMinutes(2), symbol.ExpiryTime.Content.ToDateTimeOffset());
    }

    [Fact]
    public void CreateSymbolUpdate_WithoutSymbolCode_LeavesIdentifierUnset()
    {
        // Arrange
        var track = new TrackReport("id", "NAME", 0, 0, null, null, null, null);

        // Act
        var update = TrackUpdateFactory.CreateSymbolUpdate(
            track, "R", string.Empty, SymbolCatalog.Unspecified, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        // Assert
        Assert.Null(update.Symbol.SymbolIdentifier);
        Assert.Null(update.Symbol.Location.Content.Point.GeoPoint.VerticalDistance);
    }
}
