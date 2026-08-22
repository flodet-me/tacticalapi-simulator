using Microsoft.Extensions.Logging.Abstractions;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="LoadGeneratorSource" />
///     (src/adapter/TacticalApi.Simulator.Sources.Synthetic/LoadGeneratorSource.cs).
/// </summary>
public sealed class LoadGeneratorSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static LoadGeneratorSource CreateSource(LoadGeneratorOptions options, TimeProvider? time = null)
    {
        return new LoadGeneratorSource(
            TestHelpers.Options(options),
            time ?? new TestHelpers.MutableTimeProvider(T0),
            NullLogger<LoadGeneratorSource>.Instance);
    }

    [Fact]
    public async Task ProduceAsync_EmitsOneWindowPerCycle()
    {
        // Arrange - batch size, not object count, is what bounds a single message.
        var source = CreateSource(new LoadGeneratorOptions { ObjectCount = 1000, BatchSize = 250 });

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(250, updates.Count);
    }

    [Fact]
    public async Task ProduceAsync_WalksThePopulationAndWrapsAround()
    {
        // Arrange
        var source = CreateSource(new LoadGeneratorOptions { ObjectCount = 6, BatchSize = 4 });

        // Act
        var first = await source.ProduceAsync(CancellationToken.None);
        var second = await source.ProduceAsync(CancellationToken.None);

        // Assert - the window advances, then wraps back to the start of the population.
        Assert.Equal(["load:0000000", "load:0000001", "load:0000002", "load:0000003"], Ids(first));
        Assert.Equal(["load:0000004", "load:0000005", "load:0000000", "load:0000001"], Ids(second));
    }

    [Fact]
    public async Task ProduceAsync_NeverEmitsMoreThanThePopulation()
    {
        // Arrange
        var source = CreateSource(new LoadGeneratorOptions { ObjectCount = 3, BatchSize = 500 });

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, updates.Count);
    }

    [Fact]
    public async Task ProduceAsync_MovesObjectsBetweenCycles()
    {
        // Arrange - an object that never changes is merged away as a no-op by any
        // correct implementation, so a load generator emitting static objects would
        // measure nothing at all.
        var time = new TestHelpers.MutableTimeProvider(T0);
        var source = CreateSource(new LoadGeneratorOptions { ObjectCount = 1, BatchSize = 1 }, time);

        // Act
        var first = await source.ProduceAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(5));
        var second = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var before = first[0].Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate;
        var after = second[0].Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate;
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task ProduceAsync_IsDeterministicForTheSameSettings()
    {
        // Arrange - a throughput comparison between two runs has to measure the change
        // under test, not a different random load.
        var options = new LoadGeneratorOptions { ObjectCount = 20, BatchSize = 20 };

        // Act
        var first = await CreateSource(options).ProduceAsync(CancellationToken.None);
        var second = await CreateSource(options).ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            first.Select(u => u.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate),
            second.Select(u => u.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate));
    }

    [Fact]
    public async Task ProduceAsync_UpdatesIngestCleanlyIntoTheStore()
    {
        // Arrange
        var source = CreateSource(new LoadGeneratorOptions { ObjectCount = 50, BatchSize = 50 });
        var store = TestHelpers.CreateStore();

        // Act
        var result = store.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(50, store.GetSnapshot().Count);
    }

    private static IEnumerable<string> Ids(IEnumerable<Rheinmetall.TacticalApi.V0.UpdateSituationObject> updates)
    {
        return updates.Select(u => u.Symbol.Identity.StringIdentity);
    }
}
