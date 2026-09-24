using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="CombatOutpostDefenseSource" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/CombatOutpostDefenseSource.cs).
/// </summary>
public sealed class CombatOutpostDefenseSourceTests
{
    private static CombatOutpostDefenseSource CreateSource(
        CombatOutpostDefenseOptions? options = null, TimeProvider? timeProvider = null)
    {
        return new CombatOutpostDefenseSource(TestHelpers.Options(options ?? new CombatOutpostDefenseOptions()),
            timeProvider ?? new TestHelpers.MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<CombatOutpostDefenseSource>.Instance);
    }

    [Fact]
    public async Task ProduceAsync_AlwaysEmitsPerimeterAndDefendTaskButNotTheManningItself()
    {
        // The perimeter is a graphic the commander drew and the defend task is an
        // order - both situation objects. The observation posts manning it report
        // themselves, which makes them blue forces instead.
        var options = new CombatOutpostDefenseOptions { ObservationPostCount = 3, DayContactProbability = 0 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Contains(updates, u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity == "cop:perimeter");
        Assert.Contains(updates, u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask &&
            u.ActionTask.Identity.StringIdentity == "cop:task:defend");
        Assert.DoesNotContain(updates, u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity.StartsWith("cop:op:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_EmitsTheCommandPostAndEveryObservationPost()
    {
        // Arrange
        var options = new CombatOutpostDefenseOptions { ObservationPostCount = 3, DayContactProbability = 0 };
        var source = CreateSource(options);

        // Act
        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);

        // Assert - one CP plus one per OP.
        Assert.Equal(options.ObservationPostCount + 1, blueForces.Count);
        Assert.All(blueForces, bf =>
        {
            Assert.NotNull(bf.LastContactTime);
            Assert.NotNull(bf.PointLocation?.GeoPoint);
        });

        var commandPost = Assert.Single(blueForces, bf => bf.BlueForceType.IsLeader);
        Assert.Equal(CombatOutpostDefenseSource.OwnBlueForceIdentity, commandPost.Identity.StringIdentity);
        Assert.Equal(options.ObservationPostCount,
            blueForces.Count(bf => bf.Identity.StringIdentity.StartsWith("cop:bf:op:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_ReportsGarrisonStrengthOnTheCommandPost()
    {
        // The number that says whether the COP is still holding, carried where the
        // contract puts text about a blue force.
        var source = CreateSource(new CombatOutpostDefenseOptions { GarrisonStrength = 40, DayContactProbability = 0 });

        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);
        var commandPost = Assert.Single(blueForces, bf => bf.BlueForceType.IsLeader);

        Assert.Contains("40/40 effective", commandPost.Callsign, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_ZeroContactProbability_NeverRaisesContact()
    {
        // Arrange
        var options = new CombatOutpostDefenseOptions { DayContactProbability = 0 };
        var source = CreateSource(options);

        // Act: several cycles, not just one.
        for (var i = 0; i < 5; i++)
        {
            var updates = await source.ProduceAsync(CancellationToken.None);
            Assert.DoesNotContain(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
        }
    }

    [Fact]
    public async Task ProduceAsync_GuaranteedContact_AssaultBranch_RaisesGroundAssault()
    {
        // Arrange: probability-1 thresholds make the branch selection deterministic regardless of seed.
        var options = new CombatOutpostDefenseOptions
        {
            DayContactProbability = 1,
            NightContactProbabilityMultiplier = 1,
            AssaultProbabilityGivenContact = 1
        };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var contactEvent = Assert.Single(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
        Assert.Equal(ActionEventType.Ambush, contactEvent.ActionEvent.ActionEventType.Content);
        Assert.Equal(5, contactEvent.ActionEvent.ThreatLevel.Content);
    }

    [Fact]
    public async Task ProduceAsync_GuaranteedContact_IndirectFireBranch_RaisesArtilleryFire()
    {
        // Arrange
        var options = new CombatOutpostDefenseOptions
        {
            DayContactProbability = 1,
            NightContactProbabilityMultiplier = 1,
            AssaultProbabilityGivenContact = 0,
            IndirectFireProbabilityGivenContact = 1
        };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var contactEvent = Assert.Single(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
        Assert.Equal(ActionEventType.ArtilleryFire, contactEvent.ActionEvent.ActionEventType.Content);
        Assert.Equal(SymbolLocation.LocationOneofCase.Ellipse,
            contactEvent.ActionEvent.Location.Content.LocationCase);
    }

    [Fact]
    public async Task ProduceAsync_GuaranteedContact_ProbeBranch_RaisesSniperAttack()
    {
        // Arrange
        var options = new CombatOutpostDefenseOptions
        {
            DayContactProbability = 1,
            NightContactProbabilityMultiplier = 1,
            AssaultProbabilityGivenContact = 0,
            IndirectFireProbabilityGivenContact = 0
        };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var contactEvent = Assert.Single(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
        Assert.Equal(ActionEventType.SniperAttack, contactEvent.ActionEvent.ActionEventType.Content);
        Assert.Equal(SymbolLocation.LocationOneofCase.Fan, contactEvent.ActionEvent.Location.Content.LocationCase);
    }

    [Fact]
    public async Task ProduceAsync_DefendTaskPriority_IsHigherAtNightThanDuringDay()
    {
        // Arrange: noon UTC (day) vs 2200 UTC (night, given the default 19-06 window).
        var dayTime = new TestHelpers.MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var nightTime = new TestHelpers.MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero));
        var options = new CombatOutpostDefenseOptions { DayContactProbability = 0 };

        // Act
        var dayTask = (await CreateSource(options, dayTime).ProduceAsync(CancellationToken.None))
            .Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask &&
                         u.ActionTask.Identity.StringIdentity == "cop:task:defend");
        var nightTask = (await CreateSource(options, nightTime).ProduceAsync(CancellationToken.None))
            .Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask &&
                         u.ActionTask.Identity.StringIdentity == "cop:task:defend");

        // Assert
        Assert.Equal(ActionTaskPriorityType.Priority3, dayTask.ActionTask.ActionTaskPriority.Content);
        Assert.Equal(ActionTaskPriorityType.Priority1, nightTask.ActionTask.ActionTaskPriority.Content);
    }
}
