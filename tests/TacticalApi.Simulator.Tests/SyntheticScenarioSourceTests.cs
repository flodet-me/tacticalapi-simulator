using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SyntheticScenarioSource" />
///     (src/TacticalApi.Simulator.Sources.Synthetic/SyntheticScenarioSource.cs).
/// </summary>
public sealed class SyntheticScenarioSourceTests
{
    private static SyntheticScenarioSource CreateSource(SyntheticScenarioOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        return new SyntheticScenarioSource(TestHelpers.Options(options ?? new SyntheticScenarioOptions()),
            timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public async Task ProduceAsync_EmitsAllElevenObjectTypes()
    {
        // Arrange
        var source = CreateSource(new SyntheticScenarioOptions { EventProbability = 1, ChatProbability = 1 });

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var cases = updates.Select(u => u.TypeCase).ToHashSet();

        // Assert
        var allCases = Enum.GetValues<UpdateSituationObject.TypeOneofCase>()
            .Where(c => c != UpdateSituationObject.TypeOneofCase.None);
        foreach (var oneofCase in allCases) Assert.Contains(oneofCase, cases);
    }

    [Fact]
    public async Task ProduceAsync_UpdatesIngestCleanlyIntoTheStore()
    {
        // Arrange
        var source = CreateSource(new SyntheticScenarioOptions { EventProbability = 1, ChatProbability = 1 });
        var store = TestHelpers.CreateStore();

        // Act
        var result = store.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(store.GetSnapshot().Count >= 11);
    }

    [Fact]
    public async Task ProduceAsync_OverlayContainsMaterializedPhaseLines()
    {
        // Arrange
        var source = CreateSource();
        var store = TestHelpers.CreateStore();

        // Act
        store.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));

        // Assert
        var overlay = store.GetSnapshot()
            .Single(o => o.TypeCase == SituationObject.TypeOneofCase.OverlayDocument)
            .OverlayDocument;
        Assert.Equal(2, overlay.OverlayData.Contents.Count);
        Assert.All(overlay.OverlayData.Contents, nested =>
            Assert.Equal(SituationObject.TypeOneofCase.Symbol, nested.TypeCase));
    }

    [Fact]
    public async Task ProduceAsync_ActionTaskProgressStaysInRange()
    {
        // Arrange
        var source = CreateSource();

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var task = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask).ActionTask;

        // Assert
        Assert.InRange(task.CompletionRatio.Content ?? -1, 0, 100);
        Assert.NotEqual(ActionTaskStatusType.Unspecified, task.ActionTaskStatus.Content);
    }

    [Fact]
    public async Task ProduceAsync_MidLap_ActionTaskStatusIsInProgress()
    {
        // Arrange: the epoch is captured at construction, so advancing "now"
        // afterwards moves the lap fraction into the 0.02-0.98 (InProgress) band.
        var options = new SyntheticScenarioOptions { PatrolLapDuration = TimeSpan.FromMinutes(10) };
        var time = new TestHelpers.MutableTimeProvider(DateTimeOffset.UtcNow);
        var source = CreateSource(options, time);
        time.Advance(TimeSpan.FromMinutes(5)); // 50% through the lap

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var task = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask).ActionTask;

        // Assert
        Assert.Equal(ActionTaskStatusType.InProgress, task.ActionTaskStatus.Content);
    }

    [Fact]
    public async Task ProduceAsync_NearEndOfLap_ActionTaskStatusIsComplete()
    {
        // Arrange
        var options = new SyntheticScenarioOptions { PatrolLapDuration = TimeSpan.FromMinutes(10) };
        var time = new TestHelpers.MutableTimeProvider(DateTimeOffset.UtcNow);
        var source = CreateSource(options, time);
        time.Advance(TimeSpan.FromMinutes(9.9)); // 99% through the lap

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);
        var task = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask).ActionTask;

        // Assert
        Assert.Equal(ActionTaskStatusType.Complete, task.ActionTaskStatus.Content);
    }
}
