using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Sources;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SimulationSourceRunner{TSource}" />
///     (src/TacticalApi.Simulator.Core/Sources/SimulationSourceRunner.cs), driven
///     through the real <c>BackgroundService</c> Start/StopAsync lifecycle with a
///     fast, fake <see cref="ISimulationSource" />.
/// </summary>
public sealed class SimulationSourceRunnerTests
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteAsync_SourceDisabled_NeverCallsProduce()
    {
        // Arrange
        var source = new FakeSource(FastInterval, (_, _) => Task.FromResult<IReadOnlyList<UpdateSituationObject>>([]),
            false);
        var ingest = new FakeIngest();
        var runner = CreateRunner(source, ingest);

        // Act: let it run through several would-be poll cycles.
        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await runner.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, source.CallCount);
        Assert.Empty(ingest.AddOrUpdateCalls);
    }

    [Fact]
    public async Task ExecuteAsync_EnabledSource_ProducesAndIngestsUpdates()
    {
        // Arrange
        var update = TestHelpers.SymbolUpdate("t1", DateTimeOffset.UtcNow);
        var source = new FakeSource(FastInterval,
            (_, _) => Task.FromResult<IReadOnlyList<UpdateSituationObject>>([update]));
        var ingest = new FakeIngest();
        var runner = CreateRunner(source, ingest);

        // Act
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => ingest.AddOrUpdateCalls.Count > 0);
        await runner.StopAsync(CancellationToken.None);

        // Assert
        Assert.Same(update, Assert.Single(ingest.AddOrUpdateCalls[0]));
    }

    [Fact]
    public async Task ExecuteAsync_IngestFails_LogsWarningButKeepsPolling()
    {
        // Arrange
        var source = new FakeSource(FastInterval,
            (_, _) => Task.FromResult<IReadOnlyList<UpdateSituationObject>>(
                [TestHelpers.SymbolUpdate("t1", DateTimeOffset.UtcNow)]));
        var ingest = new FakeIngest { NextResult = IngestResult.Fail("boom") };
        var runner = CreateRunner(source, ingest);

        // Act: a failed ingest must not stop the loop.
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => source.CallCount >= 2);
        await runner.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(source.CallCount >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_ProduceThrows_LogsAndRetriesNextCycle()
    {
        // Arrange: the first cycle throws, later cycles succeed.
        var source = new FakeSource(FastInterval, (count, _) => count == 1
            ? throw new InvalidOperationException("boom")
            : Task.FromResult<IReadOnlyList<UpdateSituationObject>>([]));
        var ingest = new FakeIngest();
        var runner = CreateRunner(source, ingest);

        // Act
        await runner.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => source.CallCount >= 2);
        await runner.StopAsync(CancellationToken.None);

        // Assert: the loop survived the exception and kept polling.
        Assert.True(source.CallCount >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledWhileProducing_StopsWithoutThrowing()
    {
        // Arrange: ProduceAsync awaits the runner's own stopping token, so
        // stopping the service cancels it mid-produce (OperationCanceledException
        // catch/break path, as opposed to the disabled-source delay).
        var enteredProduce = new TaskCompletionSource();
        var source = new FakeSource(FastInterval, async (_, ct) =>
        {
            enteredProduce.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var ingest = new FakeIngest();
        var runner = CreateRunner(source, ingest);

        // Act
        await runner.StartAsync(CancellationToken.None);
        await enteredProduce.Task.WaitAsync(PollTimeout);
        var stopTask = runner.StopAsync(CancellationToken.None);

        // Assert: StopAsync completes (doesn't hang or throw) despite the
        // cancellation surfacing inside ProduceAsync.
        var exception = await Record.ExceptionAsync(() => stopTask.WaitAsync(PollTimeout));
        Assert.Null(exception);
    }

    private static SimulationSourceRunner<FakeSource> CreateRunner(FakeSource source, ISituationIngest ingest)
    {
        return new SimulationSourceRunner<FakeSource>(source, ingest,
            NullLogger<SimulationSourceRunner<FakeSource>>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(PollTimeout);
        while (!condition() && !cts.IsCancellationRequested) await Task.Delay(5, CancellationToken.None);
        Assert.True(condition(), "Condition was not met within the poll timeout.");
    }

    private sealed class FakeSource(
        TimeSpan interval,
        Func<int, CancellationToken, Task<IReadOnlyList<UpdateSituationObject>>> produce,
        bool enabled = true) : ISimulationSource
    {
        private int _callCount;
        public int CallCount => _callCount;

        public string Name => "Fake";
        public bool Enabled { get; } = enabled;
        public TimeSpan Interval { get; } = interval;

        public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return produce(_callCount, cancellationToken);
        }
    }

    private sealed class FakeIngest : ISituationIngest
    {
        public List<IReadOnlyList<UpdateSituationObject>> AddOrUpdateCalls { get; } = [];
        public IngestResult NextResult { get; set; } = IngestResult.Ok;

        public Task<IngestResult> AddOrUpdateAsync(
            IReadOnlyList<UpdateSituationObject> updates, CancellationToken cancellationToken = default)
        {
            AddOrUpdateCalls.Add(updates);
            return Task.FromResult(NextResult);
        }

        public Task<IngestResult> DeleteAsync(
            IReadOnlyList<DeleteSituationObject> deletes, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IngestResult.Ok);
        }
    }
}
