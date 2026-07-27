using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="LanchesterModel" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/LanchesterModel.cs).
/// </summary>
public sealed class LanchesterModelTests
{
    [Fact]
    public void Resolve_EqualForcesAndEffectiveness_ProducesSymmetricCasualties()
    {
        var outcome = LanchesterModel.Resolve(
            friendlyStrength: 20, hostileStrength: 20,
            friendlyEffectiveness: 0.2, hostileEffectiveness: 0.2);

        Assert.Equal(outcome.HostileCasualties, outcome.FriendlyCasualties);
        Assert.Equal(outcome.HostileRemaining, outcome.FriendlyRemaining);
    }

    [Fact]
    public void Resolve_OverwhelmingFriendlyAdvantage_HostileTakesFarMoreCasualties()
    {
        var outcome = LanchesterModel.Resolve(
            friendlyStrength: 50, hostileStrength: 5,
            friendlyEffectiveness: 0.3, hostileEffectiveness: 0.1);

        Assert.True(outcome.HostileCasualties > outcome.FriendlyCasualties);
        Assert.True(outcome.HostileRemaining < outcome.FriendlyRemaining);
    }

    [Fact]
    public void Resolve_NeverProducesNegativeRemainingOrCasualties()
    {
        var outcome = LanchesterModel.Resolve(
            friendlyStrength: 3, hostileStrength: 40,
            friendlyEffectiveness: 0.05, hostileEffectiveness: 0.9, ticks: 20);

        Assert.InRange(outcome.FriendlyRemaining, 0, 3);
        Assert.InRange(outcome.HostileRemaining, 0, 40);
        Assert.InRange(outcome.FriendlyCasualties, 0, 3);
        Assert.InRange(outcome.HostileCasualties, 0, 40);
    }

    [Fact]
    public void Resolve_ZeroEffectivenessOnBothSides_NoCasualties()
    {
        var outcome = LanchesterModel.Resolve(
            friendlyStrength: 10, hostileStrength: 10,
            friendlyEffectiveness: 0, hostileEffectiveness: 0);

        Assert.Equal(0, outcome.FriendlyCasualties);
        Assert.Equal(0, outcome.HostileCasualties);
    }
}
