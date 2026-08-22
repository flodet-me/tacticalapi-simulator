using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <c>SituationStore.Clear</c>, the reset behind the Host's
///     control endpoint (src/simulator/TacticalApi.Simulator.Core/Store/SituationStore.cs).
/// </summary>
public sealed class SituationStoreClearTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Clear_EmptiesTheSituationAndReportsWhatItDropped()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0), TestHelpers.SymbolUpdate("track-2", T0)]);

        // Act
        var dropped = store.Clear();

        // Assert
        Assert.Equal(2, dropped);
        Assert.Empty(store.GetSnapshot());
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Clear_DoesNotAnnounceDeletesToSubscribers()
    {
        // Arrange - a reset restarts the situation; it is not a bulk delete, and
        // announcing it as one would be indistinguishable to a client from every
        // object really having been deleted.
        var broker = TestHelpers.CreateBroker();
        var store = TestHelpers.CreateStore(broker: broker);
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        using var subscription = broker.Subscribe();

        // Act
        store.Clear();

        // Assert
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void Clear_ForgetsTheLastReportingTimeSoOldTimestampsWorkAgain()
    {
        // Arrange - without clearing the last-write-wins bookkeeping, re-adding an
        // object after a reset would be rejected as stale by its own history.
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddHours(1), "LATER")]);
        store.Clear();

        // Act
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "EARLIER")]);

        // Assert
        Assert.Equal("EARLIER", store.GetSnapshot()[0].Symbol.Name.Content);
    }
}
