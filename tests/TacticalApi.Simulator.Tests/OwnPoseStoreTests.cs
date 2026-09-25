using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Control;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="TacticalApi.Simulator.Core.Store.OwnPoseStore" />.
///     Two behaviours carry the weight: which of several sources is handed back as
///     primary, and the contract's expiry rule - a fix that stops being refreshed
///     keeps its coordinates and is flagged, rather than disappearing.
/// </summary>
public sealed class OwnPoseStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetPosition_ReturnsNothingBeforeAnySourceHasReported()
    {
        var store = TestHelpers.CreateOwnPoseStore();

        Assert.Null(store.GetPosition(Now));
        Assert.Equal(0, store.SourceCount);
    }

    [Fact]
    public void UpdatePosition_RoundTripsTheFixItWasGiven()
    {
        var store = TestHelpers.CreateOwnPoseStore();

        var result = store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.137, 11.575), Now);

        Assert.True(result.Success);
        var position = store.GetPosition(Now);
        Assert.NotNull(position);
        Assert.Equal("GNSS", position.SourceIdentifier);
        Assert.Equal(48.137, position.PointLocation.GeoPoint.LatitudeCoordinate);
        Assert.False(position.IsInvalidOrExpired);
    }

    [Fact]
    public void UpdatePosition_RejectsAFixWithNoSourceIdentifier()
    {
        var store = TestHelpers.CreateOwnPoseStore();

        var result = store.UpdatePosition(new UpdatePosition(), Now);

        Assert.False(result.Success);
        Assert.Contains("source_identifier", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatePosition_RejectsAMissingPositionRatherThanThrowing()
    {
        // The RPC's own field is optional on the wire, so a client can express this
        // mistake and deserves an error header rather than a broken call.
        var store = TestHelpers.CreateOwnPoseStore();

        Assert.False(store.UpdatePosition(null, Now).Success);
    }

    [Fact]
    public void GetPosition_DefaultsToTheMostRecentlyReportedSource()
    {
        // No primary configured: the newest fix wins, which is the behaviour that
        // needs no configuration at all to look right with a single sensor.
        var store = TestHelpers.CreateOwnPoseStore();
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);
        store.UpdatePosition(TestHelpers.PositionUpdate("DR", 48.2, 11.6), Now);

        Assert.Equal("DR", store.GetPosition(Now)!.SourceIdentifier);

        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.3, 11.7), Now);
        Assert.Equal("GNSS", store.GetPosition(Now)!.SourceIdentifier);
    }

    [Fact]
    public void GetPosition_HonoursTheConfiguredPrimarySourceEvenWhenAnotherIsNewer()
    {
        var store = TestHelpers.CreateOwnPoseStore(new SimulatorOptions
        {
            OwnPose = new OwnPoseOptions { PrimarySource = "GNSS" }
        });

        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);
        store.UpdatePosition(TestHelpers.PositionUpdate("DR", 48.2, 11.6), Now.AddSeconds(1));

        var position = store.GetPosition(Now.AddSeconds(1));
        Assert.Equal("GNSS", position!.SourceIdentifier);
        Assert.Equal(48.1, position.PointLocation.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void GetPosition_ReturnsNothingWhenTheConfiguredPrimaryHasNeverReported()
    {
        // Substituting another sensor would hide exactly the misconfiguration a
        // client is likeliest to hit.
        var store = TestHelpers.CreateOwnPoseStore(new SimulatorOptions
        {
            OwnPose = new OwnPoseOptions { PrimarySource = "GNSS" }
        });

        store.UpdatePosition(TestHelpers.PositionUpdate("DR", 48.2, 11.6), Now);

        Assert.Null(store.GetPosition(Now));
    }

    [Fact]
    public void GetPosition_KeepsTheCoordinatesButFlagsThemOnceTheFixHasGoneStale()
    {
        // "the old position continues to be used but is considered outdated".
        var store = TestHelpers.CreateOwnPoseStore(new SimulatorOptions
        {
            OwnPose = new OwnPoseOptions { PositionTimeout = TimeSpan.FromSeconds(30) }
        });
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.137, 11.575), Now);

        Assert.False(store.GetPosition(Now.AddSeconds(29))!.IsInvalidOrExpired);

        var expired = store.GetPosition(Now.AddSeconds(30));
        Assert.True(expired!.IsInvalidOrExpired);
        Assert.Equal(48.137, expired.PointLocation.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void GetPosition_FlagsAFixThatCarriesNoCoordinatesAtAll()
    {
        // Otherwise a client cannot tell an unset position from a valid one at 0/0.
        var store = TestHelpers.CreateOwnPoseStore();
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS"), Now);

        Assert.True(store.GetPosition(Now)!.IsInvalidOrExpired);
    }

    [Fact]
    public void UpdatePosition_PublishesOnlyWhenThePrimaryPositionActuallyChanged()
    {
        var broker = TestHelpers.CreateOwnPoseBroker();
        var store = TestHelpers.CreateOwnPoseStore(broker: broker);
        using var subscription = broker.Subscribe();

        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);
        Assert.True(subscription.Reader.TryRead(out var first));
        Assert.Equal("GNSS", first.SourceIdentifier);

        // Same source, same coordinates, same validity: nothing changed, so a
        // subscriber that is redrawing on every event has nothing to redraw.
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void RefreshValidity_AnnouncesTheFlipToExpiredWithoutAnyNewUpdate()
    {
        // The whole reason the staleness sweeper exists: a subscriber is by
        // definition not calling GetPosition, so without this it would sit on a fix
        // that quietly stopped being true.
        var broker = TestHelpers.CreateOwnPoseBroker();
        var store = TestHelpers.CreateOwnPoseStore(
            new SimulatorOptions { OwnPose = new OwnPoseOptions { PositionTimeout = TimeSpan.FromSeconds(30) } },
            broker);
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);

        using var subscription = broker.Subscribe();

        Assert.False(store.RefreshValidity(Now.AddSeconds(10)));
        Assert.True(store.RefreshValidity(Now.AddSeconds(31)));

        Assert.True(subscription.Reader.TryRead(out var announced));
        Assert.True(announced.IsInvalidOrExpired);
        Assert.Equal(48.1, announced.PointLocation.GeoPoint.LatitudeCoordinate);

        // And it says so only once.
        Assert.False(store.RefreshValidity(Now.AddSeconds(32)));
    }

    [Fact]
    public void UpdatePosition_IsRejectedWhilePaused()
    {
        var pause = new SimulationPause();
        var store = TestHelpers.CreateOwnPoseStore(pause: pause);
        pause.Pause();

        Assert.False(store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now).Success);
        Assert.Null(store.GetPosition(Now));
    }

    [Fact]
    public void Clear_DropsEverySource()
    {
        var store = TestHelpers.CreateOwnPoseStore();
        store.UpdatePosition(TestHelpers.PositionUpdate("GNSS", 48.1, 11.5), Now);
        store.UpdatePosition(TestHelpers.PositionUpdate("DR", 48.2, 11.6), Now);

        Assert.Equal(2, store.Clear());
        Assert.Null(store.GetPosition(Now));
        Assert.Equal(0, store.SourceCount);
    }
}
