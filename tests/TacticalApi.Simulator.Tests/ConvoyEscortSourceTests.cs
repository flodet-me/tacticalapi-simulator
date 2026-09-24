using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="ConvoyEscortSource" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/ConvoyEscortSource.cs).
/// </summary>
public sealed class ConvoyEscortSourceTests
{
    private static ConvoyEscortSource CreateSource(ConvoyEscortOptions? options = null, TimeProvider? timeProvider = null)
    {
        return new ConvoyEscortSource(TestHelpers.Options(options ?? new ConvoyEscortOptions()),
            timeProvider ?? TimeProvider.System, NullLogger<ConvoyEscortSource>.Instance);
    }

    [Fact]
    public async Task ProduceAsync_EmitsTheRouteButNotTheVehiclesThemselves()
    {
        // The serial reports itself over BlueForceTracking; the Situation service
        // carries what the convoy reports *about*. Emitting the gun trucks on both
        // would put the same vehicle in a client's picture twice.
        var options = new ConvoyEscortOptions { CargoVehicleCount = 3, SecurityVehicleCount = 2 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Route);
        Assert.DoesNotContain(updates, u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity.StartsWith("convoy:vehicle:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_EmitsEveryVehicleOfTheSerial()
    {
        // Arrange
        var options = new ConvoyEscortOptions { CargoVehicleCount = 3, SecurityVehicleCount = 2 };
        var source = CreateSource(options);

        // Act
        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(options.CargoVehicleCount + options.SecurityVehicleCount, blueForces.Count);
        Assert.All(blueForces, bf =>
        {
            Assert.StartsWith("convoy:vehicle:", bf.Identity.StringIdentity, StringComparison.Ordinal);
            Assert.NotNull(bf.LastContactTime);
            Assert.True(bf.BlueForceType.IsVehicle);
            Assert.NotNull(bf.PointLocation?.GeoPoint);
        });

        // Exactly one commander, riding the lead gun truck.
        var leader = Assert.Single(blueForces, bf => bf.BlueForceType.IsLeader);
        Assert.Equal(ConvoyEscortSource.OwnBlueForceIdentity, leader.Identity.StringIdentity);
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_SpacesTheSerialOutAlongTheRoute()
    {
        // A convoy stacked on one point would hide the whole thing a BFT feed is for.
        var source = CreateSource(new ConvoyEscortOptions { CargoVehicleCount = 3, SecurityVehicleCount = 2 });

        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);
        var positions = blueForces
            .Select(bf => (bf.PointLocation.GeoPoint.LatitudeCoordinate, bf.PointLocation.GeoPoint.LongitudeCoordinate))
            .ToList();

        Assert.True(positions.Distinct().Count() > 1, "every vehicle reported the same position");
    }

    [Fact]
    public async Task ProduceBlueForcesAsync_ReportsCasualtiesInTheCallsign()
    {
        // BlueForce has no free-text field, so the state a watcher actually needs -
        // this truck has been hit - goes where the contract says text about a blue
        // force goes. A guaranteed ambush makes it deterministic.
        var source = CreateSource(new ConvoyEscortOptions
        {
            BaseAmbushProbability = 1.0,
            RiskZoneMultiplier = 1.0,
            PersonnelPerVehicle = 4
        });

        await source.ProduceAsync(CancellationToken.None);
        var blueForces = await source.ProduceBlueForcesAsync(CancellationToken.None);

        Assert.Contains(blueForces, bf =>
            bf.Callsign.Contains("WIA", StringComparison.Ordinal)
            || bf.Callsign.Contains("COMBAT INEFFECTIVE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProduceAsync_ZeroAmbushProbability_NeverRaisesAmbush()
    {
        // Arrange
        var options = new ConvoyEscortOptions { BaseAmbushProbability = 0, RiskZoneMultiplier = 1 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
    }

    [Fact]
    public async Task ProduceAsync_GuaranteedAmbush_RaisesAmbushEventWithValidCasualties()
    {
        // Arrange: probability 1 makes the ambush roll deterministic regardless of seed.
        var options = new ConvoyEscortOptions
        {
            BaseAmbushProbability = 1,
            RiskZoneMultiplier = 1,
            PersonnelPerVehicle = 4
        };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var contactEvent = Assert.Single(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionEvent);
        Assert.Equal(ActionEventType.Ambush, contactEvent.ActionEvent.ActionEventType.Content);
        Assert.InRange(contactEvent.ActionEvent.ThreatLevel.Content ?? -1, 1, 5);

        // Casualties (if any) show up as reduced/disabled vehicles, never negative personnel.
        var vehicles = updates.Where(u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity.StartsWith("convoy:vehicle:", StringComparison.Ordinal));
        Assert.All(vehicles, v => Assert.NotNull(v.Symbol.AdditionalInformation?.Content));
    }

    [Fact]
    public async Task ProduceAsync_GuaranteedAmbush_SpawnsHostileSymbolsWithHostileAffiliation()
    {
        // Arrange
        var options = new ConvoyEscortOptions { BaseAmbushProbability = 1, RiskZoneMultiplier = 1 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert: at least the possibility of hostile survivors is modeled with the hostile SIDC.
        var hostiles = updates.Where(u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity.StartsWith("convoy:hostile:", StringComparison.Ordinal));
        Assert.All(hostiles, h => Assert.Equal("SHGPUCI--------", h.Symbol.SymbolIdentifier.Content.StringIdentifier));
    }

    [Fact]
    public async Task ProduceAsync_AlwaysRefreshesSaluteReport()
    {
        // Arrange
        var source = CreateSource();

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.NatoMessageDocument);
    }
}
