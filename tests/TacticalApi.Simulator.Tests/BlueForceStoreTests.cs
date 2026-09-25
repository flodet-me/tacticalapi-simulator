using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Control;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="TacticalApi.Simulator.Core.Store.BlueForceStore" />.
///     The centre of gravity here is the one rule that makes this service behave
///     unlike the situation store it sits next to: every call replaces the blue force
///     whole. Sharing a process with <c>SituationStoreTests</c> makes it easy to
///     assume the merge semantics carry over, and they deliberately do not.
/// </summary>
public sealed class BlueForceStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddOrUpdate_StoresTheBlueForceAndReturnsItInTheSnapshot()
    {
        // Arrange
        var store = TestHelpers.CreateBlueForceStore();

        // Act
        var result = store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now, "ALPHA", 52.5, 13.4)]);

        // Assert
        Assert.True(result.Success);
        var stored = Assert.Single(store.GetSnapshot());
        Assert.Equal("ALPHA", stored.Callsign);
        Assert.Equal(52.5, stored.PointLocation.GeoPoint.LatitudeCoordinate);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public void AddOrUpdate_ReplacesEveryFieldRatherThanMergingThem()
    {
        // The contract is explicit: "all fields must be filled in every call". A
        // second report that omits the callsign means the blue force no longer has
        // one - not that the previous value survives, which is what the situation
        // store next door would do.
        var store = TestHelpers.CreateBlueForceStore();
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now, "ALPHA", 52.5, 13.4,
            update => update.MountHost = new Identity { StringIdentity = "bf:carrier" })]);

        // Act
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now.AddSeconds(1), latitude: 52.6, longitude: 13.5)]);

        // Assert
        var stored = Assert.Single(store.GetSnapshot());
        Assert.Null(stored.Callsign);
        Assert.Null(stored.MountHost);
        Assert.Equal(52.6, stored.PointLocation.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void AddOrUpdate_IgnoresAReportOlderThanTheStoredLastContactTime()
    {
        // Arrange
        var store = TestHelpers.CreateBlueForceStore();
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now, "NEWER")]);

        // Act
        var result = store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now.AddHours(-1), "OLDER")]);

        // Assert - stale reports are ignored, not errors.
        Assert.True(result.Success);
        Assert.Equal("NEWER", Assert.Single(store.GetSnapshot()).Callsign);
    }

    [Fact]
    public void AddOrUpdate_RejectsAnUpdateWithNoIdentity()
    {
        var store = TestHelpers.CreateBlueForceStore();

        var result = store.AddOrUpdate([new UpdateBlueForce { LastContactTime = Timestamp.FromDateTimeOffset(Now) }]);

        Assert.False(result.Success);
        Assert.Contains("identity", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void AddOrUpdate_RejectsAnUpdateWithNoLastContactTime()
    {
        // Without it there is nothing to time out against and no ordering at all.
        var store = TestHelpers.CreateBlueForceStore();

        var result = store.AddOrUpdate(
            [new UpdateBlueForce { Identity = new Identity { StringIdentity = "bf:1" } }]);

        Assert.False(result.Success);
        Assert.Contains("last_contact_time", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrUpdate_RejectsNewBlueForcesBeyondTheConfiguredCap()
    {
        // Arrange
        var store = TestHelpers.CreateBlueForceStore(new SimulatorOptions
        {
            BlueForce = new BlueForceOptions { MaxBlueForces = 2 }
        });
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now), TestHelpers.BlueForceUpdate("bf:2", Now)]);

        // Act
        var result = store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:3", Now)]);

        // Assert - but an existing one can still keep itself alive.
        Assert.False(result.Success);
        Assert.True(store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now.AddSeconds(1))]).Success);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void AddOrUpdate_FlagsOnlyTheConfiguredOwnIdentityAsOwnBlueForce()
    {
        // own_blue_force has no field in UpdateBlueForce at all: which blue force is
        // "me" is the answering system's to decide, not the reporter's.
        var store = TestHelpers.CreateBlueForceStore(new SimulatorOptions
        {
            BlueForce = new BlueForceOptions { OwnIdentity = "bf:me" }
        });

        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:me", Now), TestHelpers.BlueForceUpdate("bf:other", Now)]);

        var snapshot = store.GetSnapshot();
        Assert.True(snapshot.Single(bf => bf.Identity.StringIdentity == "bf:me").OwnBlueForce);
        Assert.False(snapshot.Single(bf => bf.Identity.StringIdentity == "bf:other").OwnBlueForce);
    }

    [Fact]
    public void AddOrUpdate_IsRejectedWhilePaused()
    {
        var pause = new SimulationPause();
        var store = TestHelpers.CreateBlueForceStore(pause: pause);
        pause.Pause();

        var result = store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now)]);

        Assert.False(result.Success);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void SweepTimedOut_DeletesBlueForcesPastTheKeepAliveTimeoutAndLeavesTheRest()
    {
        // Arrange
        var store = TestHelpers.CreateBlueForceStore(new SimulatorOptions
        {
            BlueForce = new BlueForceOptions { KeepAliveTimeout = TimeSpan.FromSeconds(30) }
        });
        store.AddOrUpdate([
            TestHelpers.BlueForceUpdate("bf:quiet", Now),
            TestHelpers.BlueForceUpdate("bf:alive", Now.AddSeconds(29))
        ]);

        // Act
        var swept = store.SweepTimedOut(Now.AddSeconds(30));

        // Assert
        Assert.Equal(1, swept);
        Assert.Equal("bf:alive", Assert.Single(store.GetSnapshot()).Identity.StringIdentity);
    }

    [Fact]
    public void SweepTimedOut_AnnouncesTheDeletionOnceAndThenDropsTheBlueForce()
    {
        // A tombstone kept forever would leak; announcing it is what lets a client
        // learn about the deletion at all, since the snapshot only shows what is left.
        var broker = TestHelpers.CreateBlueForceBroker();
        var store = TestHelpers.CreateBlueForceStore(
            new SimulatorOptions { BlueForce = new BlueForceOptions { KeepAliveTimeout = TimeSpan.FromSeconds(30) } },
            broker);
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:quiet", Now, "ALPHA")]);

        using var subscription = broker.Subscribe();

        // Act
        store.SweepTimedOut(Now.AddMinutes(1));

        // Assert
        Assert.True(subscription.Reader.TryRead(out var announced));
        Assert.True(announced.IsDeleted);
        Assert.Equal("ALPHA", announced.Callsign);
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.SweepTimedOut(Now.AddMinutes(2)));
    }

    [Fact]
    public void SweepTimedOut_DoesNothingWhilePaused()
    {
        // A paused simulator is frozen, implicit deletion included - otherwise blue
        // forces would vanish underneath whoever paused it to look at them.
        var pause = new SimulationPause();
        var store = TestHelpers.CreateBlueForceStore(
            new SimulatorOptions { BlueForce = new BlueForceOptions { KeepAliveTimeout = TimeSpan.FromSeconds(1) } },
            pause: pause);
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now)]);
        pause.Pause();

        Assert.Equal(0, store.SweepTimedOut(Now.AddHours(1)));
        Assert.Single(store.GetSnapshot());
    }

    [Fact]
    public void AddOrUpdate_PublishesEveryAppliedChangeToSubscribers()
    {
        var broker = TestHelpers.CreateBlueForceBroker();
        var store = TestHelpers.CreateBlueForceStore(broker: broker);
        using var subscription = broker.Subscribe();

        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now), TestHelpers.BlueForceUpdate("bf:2", Now)]);

        Assert.True(subscription.Reader.TryRead(out _));
        Assert.True(subscription.Reader.TryRead(out _));
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void Clear_DropsEverythingWithoutAnnouncingDeletions()
    {
        // A reset is a restart of the situation, not a bulk delete of it - same
        // reasoning as SituationStore.Clear.
        var broker = TestHelpers.CreateBlueForceBroker();
        var store = TestHelpers.CreateBlueForceStore(broker: broker);
        store.AddOrUpdate([TestHelpers.BlueForceUpdate("bf:1", Now)]);

        using var subscription = broker.Subscribe();
        var dropped = store.Clear();

        Assert.Equal(1, dropped);
        Assert.Empty(store.GetSnapshot());
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void AddOrUpdate_DoesNotShareMutableStateWithTheRequest()
    {
        // The store hands published instances straight to subscribers without
        // cloning, so a caller mutating its own request afterwards must not be able
        // to reach inside the stored blue force.
        var store = TestHelpers.CreateBlueForceStore();
        var update = TestHelpers.BlueForceUpdate("bf:1", Now, "ALPHA", 52.5, 13.4);
        store.AddOrUpdate([update]);

        update.PointLocation.GeoPoint.LatitudeCoordinate = 0;

        Assert.Equal(52.5, Assert.Single(store.GetSnapshot()).PointLocation.GeoPoint.LatitudeCoordinate);
    }
}
