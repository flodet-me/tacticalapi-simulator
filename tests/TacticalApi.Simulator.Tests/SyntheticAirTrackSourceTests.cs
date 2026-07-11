using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

public sealed class SyntheticAirTrackSourceTests
{
    [Fact]
    public async Task ProduceAsync_EmitsConfiguredNumberOfSymbolUpdates()
    {
        var options = new SyntheticAirTrackOptions { TrackCount = 5 };
        var source = new SyntheticAirTrackSource(new StaticMonitor(options), TimeProvider.System);

        var updates = await source.ProduceAsync(CancellationToken.None);

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
        var options = new SyntheticAirTrackOptions { TrackCount = 3 };
        var source = new SyntheticAirTrackSource(new StaticMonitor(options), TimeProvider.System);

        var first = await source.ProduceAsync(CancellationToken.None);
        var second = await source.ProduceAsync(CancellationToken.None);

        Assert.Equal(
            first.Select(u => u.Symbol.Identity.StringIdentity),
            second.Select(u => u.Symbol.Identity.StringIdentity));
    }

    private sealed class StaticMonitor(SyntheticAirTrackOptions value) : IOptionsMonitor<SyntheticAirTrackOptions>
    {
        public SyntheticAirTrackOptions CurrentValue => value;

        public SyntheticAirTrackOptions Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<SyntheticAirTrackOptions, string?> listener)
        {
            return null;
        }
    }
}
