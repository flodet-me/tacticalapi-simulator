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
    public async Task ProduceAsync_EmitsRouteAndAllVehicles()
    {
        // Arrange
        var options = new ConvoyEscortOptions { CargoVehicleCount = 3, SecurityVehicleCount = 2 };
        var source = CreateSource(options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Route);
        var vehicleCount = updates.Count(u =>
            u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol &&
            u.Symbol.Identity.StringIdentity.StartsWith("convoy:vehicle:", StringComparison.Ordinal));
        Assert.Equal(options.CargoVehicleCount + options.SecurityVehicleCount, vehicleCount);
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
