using TacticalApi.Simulator.Core.Control;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for the pause switch and the store behavior it gates
///     (src/simulator/TacticalApi.Simulator.Core/Control/SimulationPause.cs).
/// </summary>
public sealed class SimulationPauseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PauseAndResume_ReportWhetherTheyChangedAnything()
    {
        // Arrange
        var pause = new SimulationPause();

        // Act & Assert - the control endpoints report this back, so a second pause
        // has to be distinguishable from the first.
        Assert.True(pause.Pause());
        Assert.False(pause.Pause());
        Assert.True(pause.IsPaused);
        Assert.True(pause.Resume());
        Assert.False(pause.Resume());
        Assert.False(pause.IsPaused);
    }

    [Fact]
    public void AddOrUpdate_IsRejectedWhilePaused()
    {
        // Arrange
        var pause = new SimulationPause();
        var store = TestHelpers.CreateStore(pause: pause);
        pause.Pause();

        // Act
        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        // Assert - rejected with an error header, not silently swallowed: a client
        // must be able to tell a frozen situation from a working one.
        Assert.False(result.Success);
        Assert.Equal(SimulationPause.PausedMessage, result.ErrorMessage);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void Delete_IsRejectedWhilePaused()
    {
        // Arrange
        var pause = new SimulationPause();
        var store = TestHelpers.CreateStore(pause: pause);
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);
        pause.Pause();

        // Act
        var result = store.Delete([TestHelpers.Delete("track-1", T0.AddMinutes(1))]);

        // Assert
        Assert.False(result.Success);
        Assert.Single(store.GetSnapshot());
    }

    [Fact]
    public void SweepExpired_DoesNothingWhilePaused()
    {
        // Arrange - a paused situation must stay exactly as it was, or pausing it to
        // look at it would change what you were looking at.
        var pause = new SimulationPause();
        var store = TestHelpers.CreateStore(pause: pause);
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, expiry: T0.AddMinutes(1))]);
        pause.Pause();

        // Act
        var swept = store.SweepExpired(T0.AddHours(1), TestHelpers.TestReporterId);

        // Assert
        Assert.Equal(0, swept);
        Assert.Single(store.GetSnapshot());

        // And resumes sweeping afterwards.
        pause.Resume();
        Assert.Equal(1, store.SweepExpired(T0.AddHours(1), TestHelpers.TestReporterId));
    }

    [Fact]
    public void Writes_ResumeAfterResume()
    {
        // Arrange
        var pause = new SimulationPause();
        var store = TestHelpers.CreateStore(pause: pause);
        pause.Pause();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        // Act
        pause.Resume();
        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        // Assert
        Assert.True(result.Success);
        Assert.Single(store.GetSnapshot());
    }
}
