using Microsoft.Extensions.Logging.Abstractions;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Store;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="ExpirySweeper" />
///     (src/TacticalApi.Simulator.Core/Store/ExpirySweeper.cs), driven through
///     the real <c>BackgroundService</c> Start/StopAsync lifecycle.
/// </summary>
public sealed class ExpirySweeperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_PeriodicallySweepsExpiredObjects()
    {
        // Arrange
        var time = new TestHelpers.MutableTimeProvider(T0);
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("expiring", T0, expiry: T0.AddSeconds(1))]);
        var options = new SimulatorOptions { ExpirySweepInterval = TimeSpan.FromMilliseconds(20) };
        var sweeper = new ExpirySweeper(store, TestHelpers.Options(options), time, NullLogger<ExpirySweeper>.Instance);
        time.Advance(TimeSpan.FromSeconds(2)); // the object above is now expired

        // Act
        await sweeper.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.GetSnapshot().Count == 0);
        await sweeper.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(store.GetSnapshot());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition() && !cts.IsCancellationRequested) await Task.Delay(5, CancellationToken.None);
        Assert.True(condition(), "Condition was not met within the poll timeout.");
    }
}
