using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SyntheticAirTrackSource" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/SyntheticAirTrackSource.cs).
/// </summary>
public sealed class SyntheticAirTrackSourceTests
{
    [Fact]
    public void Source_ExposesNameAndIntervalFromOptions()
    {
        // Arrange
        var options = new SyntheticAirTrackOptions { UpdateInterval = TimeSpan.FromSeconds(7) };
        var source = new SyntheticAirTrackSource(TestHelpers.Options(options), TimeProvider.System);

        // Act & Assert
        Assert.Equal("SyntheticAirTracks", source.Name);
        Assert.Equal(TimeSpan.FromSeconds(7), source.Interval);
    }

    [Fact]
    public async Task ProduceAsync_EmitsConfiguredNumberOfSymbolUpdates()
    {
        // Arrange
        var options = new SyntheticAirTrackOptions { TrackCount = 5 };
        var source = new SyntheticAirTrackSource(TestHelpers.Options(options), TimeProvider.System);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(5, updates.Count);
        Assert.All(updates, u =>
        {
            Assert.Equal(UpdateSituationObject.TypeOneofCase.Symbol, u.TypeCase);
            var point = u.Symbol.Location.Content.Point.GeoPoint;
            Assert.InRange(point.LatitudeCoordinate, -90, 90);
            Assert.InRange(point.LongitudeCoordinate, -180, 180);
        });
    }

    [Fact]
    public async Task ProduceAsync_TrackIdentitiesAreStableAcrossCycles()
    {
        // Arrange
        var options = new SyntheticAirTrackOptions { TrackCount = 3 };
        var source = new SyntheticAirTrackSource(TestHelpers.Options(options), TimeProvider.System);

        // Act
        var first = await source.ProduceAsync(CancellationToken.None);
        var second = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            first.Select(u => u.Symbol.Identity.StringIdentity),
            second.Select(u => u.Symbol.Identity.StringIdentity));
    }
}
