using Microsoft.Extensions.Logging.Abstractions;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="BlueForcePatrolSource" />
///     (src/adapter/TacticalApi.Simulator.Sources.Synthetic/BlueForcePatrolSource.cs) -
///     the one source that feeds the contract's BlueForceTracking and OwnPose
///     services.
/// </summary>
public sealed class BlueForcePatrolSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static BlueForcePatrolSource CreateSource(
        BlueForcePatrolOptions? options = null, TimeProvider? timeProvider = null)
    {
        return new BlueForcePatrolSource(
            TestHelpers.Options(options ?? new BlueForcePatrolOptions()),
            timeProvider ?? new TestHelpers.MutableTimeProvider(Start),
            NullLogger<BlueForcePatrolSource>.Instance);
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_EmitsTheWholeSectionPlusItsCarrierAndUas()
    {
        // Arrange
        var options = new BlueForcePatrolOptions { DismountCount = 4 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceBlueForcesAsync(CancellationToken.None);

        // Assert - carrier + UAS + leader + riflemen.
        Assert.Equal(options.DismountCount + 3, updates.Count);
        Assert.All(updates, update =>
        {
            Assert.NotNull(update.Identity);
            Assert.NotNull(update.LastContactTime);
            Assert.NotNull(update.PointLocation?.GeoPoint);
        });
        Assert.Contains(updates, u => u.Identity.StringIdentity == BlueForcePatrolSource.OwnBlueForceIdentity);
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_SetsExactlyOneLeaderOneVehicleAndOneUnmanned()
    {
        // BlueForceType allows several flags at once, which makes it easy to set the
        // wrong one everywhere and never notice - so this counts them.
        var source = CreateSource();

        var updates = await source.ProduceBlueForcesAsync(CancellationToken.None);

        Assert.Single(updates, u => u.BlueForceType?.IsLeader == true);
        Assert.Single(updates, u => u.BlueForceType?.IsVehicle == true);
        Assert.Single(updates, u => u.BlueForceType?.IsUnmanned == true);
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_NamesTheCarrierAsMountHostOnlyWhileMounted()
    {
        // Arrange - the section starts mounted and dismounts one phase later.
        var options = new BlueForcePatrolOptions
        {
            MountedPhaseDuration = TimeSpan.FromMinutes(1),
            DismountedPhaseDuration = TimeSpan.FromMinutes(1)
        };
        var time = new TestHelpers.MutableTimeProvider(Start);
        var source = CreateSource(options, time);

        // Act - mounted.
        var mounted = await source.ProduceBlueForcesAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(90));
        var dismounted = await source.ProduceBlueForcesAsync(CancellationToken.None);

        // Assert - everyone but the carrier itself names it while mounted...
        var riders = mounted.Where(u => u.Identity.StringIdentity != "blueforce:patrol:carrier").ToList();
        Assert.NotEmpty(riders);
        Assert.All(riders, u => Assert.Equal("blueforce:patrol:carrier", u.MountHost?.StringIdentity));

        // ...and nobody does once they are on the ground.
        Assert.All(dismounted, u => Assert.Null(u.MountHost));
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_MovesTheCarrierAlongItsLoopOverTime()
    {
        var time = new TestHelpers.MutableTimeProvider(Start);
        var source = CreateSource(new BlueForcePatrolOptions { LapDuration = TimeSpan.FromMinutes(10) }, time);

        var first = Carrier(await source.ProduceBlueForcesAsync(CancellationToken.None));
        time.Advance(TimeSpan.FromMinutes(2));
        var later = Carrier(await source.ProduceBlueForcesAsync(CancellationToken.None));

        Assert.NotEqual(first.LatitudeCoordinate, later.LatitudeCoordinate);
    }

    [Fact]
    public async Task ProducePositionAsync_ReportsTheLeaderFixUnderTheConfiguredSource()
    {
        var source = CreateSource(new BlueForcePatrolOptions
        {
            GnssOutageProbability = 0,
            PositionSourceIdentifier = "GNSS-1"
        });

        var position = await source.ProducePositionAsync(CancellationToken.None);

        Assert.NotNull(position);
        Assert.Equal("GNSS-1", position.SourceIdentifier);
        Assert.NotNull(position.PointLocation?.GeoPoint);
    }

    [Fact]
    public async Task ProducePositionAsync_ReportsNothingWhileTheGnssIsOut()
    {
        // The contract's own example of an expiring position. Reporting nothing is
        // the point: it leaves the server's staleness handling to show the client.
        var time = new TestHelpers.MutableTimeProvider(Start);
        var source = CreateSource(new BlueForcePatrolOptions
        {
            GnssOutageProbability = 1.0,
            GnssOutageDuration = TimeSpan.FromSeconds(30)
        }, time);

        Assert.Null(await source.ProducePositionAsync(CancellationToken.None));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(await source.ProducePositionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProducePositionAsync_ResumesOnceTheOutageHasElapsed()
    {
        var time = new TestHelpers.MutableTimeProvider(Start);
        var options = new BlueForcePatrolOptions
        {
            GnssOutageProbability = 1.0,
            GnssOutageDuration = TimeSpan.FromSeconds(30)
        };
        var source = CreateSource(options, time);

        Assert.Null(await source.ProducePositionAsync(CancellationToken.None));

        // The outage ends; the next roll is what decides whether a new one starts, so
        // drop the probability to zero to isolate the recovery itself.
        time.Advance(TimeSpan.FromSeconds(31));
        options.GnssOutageProbability = 0;

        Assert.NotNull(await source.ProducePositionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BothProducers_AgreeOnTheLeaderPosition()
    {
        // The reason the two runners share one instance. If they ever ran against
        // separate objects, each would keep its own patrol clock and the position
        // reported over OwnPose would drift away from the same soldier's blue force.
        var time = new TestHelpers.MutableTimeProvider(Start);
        var source = CreateSource(new BlueForcePatrolOptions { GnssOutageProbability = 0 }, time);

        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);
        var position = await source.ProducePositionAsync(CancellationToken.None);

        var leader = blueForces
            .Single(u => u.Identity.StringIdentity == BlueForcePatrolSource.OwnBlueForceIdentity)
            .PointLocation.GeoPoint;

        Assert.NotNull(position);
        Assert.Equal(leader.LatitudeCoordinate, position.PointLocation.GeoPoint.LatitudeCoordinate);
        Assert.Equal(leader.LongitudeCoordinate, position.PointLocation.GeoPoint.LongitudeCoordinate);
    }

    [Fact]
    public async Task BothProducers_CanBeDrivenConcurrentlyWithoutCorruptingSharedState()
    {
        // The two hosted runners tick on their own timers against this one instance,
        // so both producers genuinely do run at once in production. A Random shared
        // across threads returns garbage and can corrupt its own state, which would
        // surface only as an occasional wrong number - so hammer both and require
        // every result to stay well-formed.
        var source = CreateSource(new BlueForcePatrolOptions
        {
            // A coin-flip outage means the state machine is contended on every cycle
            // rather than sitting in one branch.
            GnssOutageProbability = 0.5,
            GnssOutageDuration = TimeSpan.FromMilliseconds(1)
        });

        var blueForceTask = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
            {
                var updates = await source.ProduceBlueForcesAsync(CancellationToken.None);
                Assert.All(updates, u => Assert.NotNull(u.PointLocation?.GeoPoint));
            }
        });

        var positionTask = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
            {
                var position = await source.ProducePositionAsync(CancellationToken.None);

                // Null is a legitimate result (the GNSS is out); a malformed one is not.
                if (position is null) continue;
                Assert.Equal("GNSS", position.SourceIdentifier);
                Assert.NotNull(position.PointLocation?.GeoPoint);
            }
        });

        await Task.WhenAll(blueForceTask, positionTask);
    }

    [Fact]
    public void Source_IsNamedAfterItsOwnConfigurationSection()
    {
        Assert.Equal("BlueForcePatrol", CreateSource().Name);
    }

    private static Rheinmetall.TacticalApi.V0.GeoPoint Carrier(
        IReadOnlyList<Rheinmetall.TacticalApi.V0.UpdateBlueForce> updates)
    {
        return updates.Single(u => u.Identity.StringIdentity == "blueforce:patrol:carrier").PointLocation.GeoPoint;
    }
}
