using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

public sealed class SyntheticScenarioSourceTests
{
    private static SyntheticScenarioSource CreateSource(SyntheticScenarioOptions? options = null)
    {
        return new SyntheticScenarioSource(new StaticMonitor(options ?? new SyntheticScenarioOptions()),
            TimeProvider.System);
    }

    [Fact]
    public async Task ProduceAsync_EmitsAllElevenObjectTypes()
    {
        var source = CreateSource(new SyntheticScenarioOptions { EventProbability = 1, ChatProbability = 1 });

        var updates = await source.ProduceAsync(CancellationToken.None);
        var cases = updates.Select(u => u.TypeCase).ToHashSet();

        var allCases = Enum.GetValues<UpdateSituationObject.TypeOneofCase>()
            .Where(c => c != UpdateSituationObject.TypeOneofCase.None);
        foreach (var oneofCase in allCases) Assert.Contains(oneofCase, cases);
    }

    [Fact]
    public async Task ProduceAsync_UpdatesIngestCleanlyIntoTheStore()
    {
        var source = CreateSource(new SyntheticScenarioOptions { EventProbability = 1, ChatProbability = 1 });
        var store = TestHelpers.CreateStore();

        var result = store.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(store.GetSnapshot().Count >= 11);
    }

    [Fact]
    public async Task ProduceAsync_OverlayContainsMaterializedPhaseLines()
    {
        var source = CreateSource();
        var store = TestHelpers.CreateStore();

        store.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));

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
        var source = CreateSource();

        var updates = await source.ProduceAsync(CancellationToken.None);
        var task = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.ActionTask).ActionTask;

        Assert.InRange(task.CompletionRatio.Content ?? -1, 0, 100);
        Assert.NotEqual(ActionTaskStatusType.Unspecified, task.ActionTaskStatus.Content);
    }

    private sealed class StaticMonitor(SyntheticScenarioOptions value) : IOptionsMonitor<SyntheticScenarioOptions>
    {
        public SyntheticScenarioOptions CurrentValue => value;

        public SyntheticScenarioOptions Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<SyntheticScenarioOptions, string?> listener)
        {
            return null;
        }
    }
}
