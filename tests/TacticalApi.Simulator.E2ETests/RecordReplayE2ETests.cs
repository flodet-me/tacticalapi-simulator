using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Sources.Replay;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for record and replay against a real host over real gRPC:
///     record a situation, reset it, replay the recording, and check the situation
///     came back (src/adapter/TacticalApi.Simulator.Sources.Replay/).
/// </summary>
public sealed class RecordReplayE2ETests : IDisposable
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tacticalapi-e2e-{Guid.NewGuid():N}");

    private string RecordingPath => Path.Combine(_directory, "recording.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task RecordThenReplay_RestoresTheSituationAfterAReset()
    {
        // Arrange - a real host, populated over the contract.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects =
            {
                E2E.Symbol("e2e:record:1", T0, "ALPHA", 53.0, 8.8),
                E2E.Symbol("e2e:record:2", T0, "BRAVO", 54.0, 9.0)
            }
        });

        // Act 1 - record it by subscribing, exactly as any client would.
        await RecordAsync(factory, TimeSpan.FromSeconds(5));

        // Act 2 - wipe the situation, then replay what was recorded.
        await http.PostAsync(new Uri("/api/control/reset", UriKind.Relative), null);
        Assert.Empty((await client.GetSituationObjectsAsync(new GetSituationObjectsRequest())).SituationObjects);

        await ReplayAsync(client);

        // Assert - the situation is back, with its content intact.
        var restored = (await client.GetSituationObjectsAsync(new GetSituationObjectsRequest())).SituationObjects;
        Assert.Equal(2, restored.Count);

        var alpha = Assert.Single(restored, o => o.Symbol.Identity.StringIdentity == "e2e:record:1");
        Assert.Equal("ALPHA", alpha.Symbol.Name.Content);
        Assert.Equal(53.0, alpha.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public async Task Recorder_RecordsDeletesAsDeletes()
    {
        // Arrange - a delete recorded as an update would resurrect the object on
        // replay, which is the opposite of what the recording captured.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol("e2e:record:deleted", T0, "ALPHA") }
        });

        // Act - record across the delete.
        var recording = RecordAsync(factory, TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await client.DeleteSituationObjectsAsync(new DeleteSituationObjectsRequest
        {
            SituationObjects = { E2E.Delete("e2e:record:deleted", T0.AddMinutes(1)) }
        });
        await recording;

        // Assert
        var frames = await RecordingReader.ReadAllAsync(RecordingPath, NullLogger.Instance);
        Assert.Contains(frames, f => f.Deletes.Any(d => d.Identity.StringIdentity == "e2e:record:deleted"));
    }

    [Fact]
    public async Task Recorder_CanSkipTheInitialSnapshot()
    {
        // Arrange - the caller's choice: reconstruct the situation as it stood, or
        // capture only what happened from here on.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol("e2e:record:pre-existing", T0, "ALPHA") }
        });

        // Act
        await RecordAsync(factory, TimeSpan.FromSeconds(2), includeInitialSnapshot: false);

        // Assert - nothing changed while recording, so with the snapshot skipped there
        // is nothing to record at all.
        Assert.False(File.Exists(RecordingPath) && new FileInfo(RecordingPath).Length > 0);
    }

    private async Task RecordAsync(
        SimulatorFactory factory, TimeSpan duration, bool includeInitialSnapshot = true)
    {
        Directory.CreateDirectory(_directory);

        using var recorder = new SituationRecorder(
            factory.CreateGrpcClient(),
            Options.Create(new RecorderOptions
            {
                Enabled = true,
                Path = RecordingPath,
                IncludeInitialSnapshot = includeInitialSnapshot
            }),
            TestOptions(new GrpcIngestOptions()),
            TimeProvider.System,
            NullLogger<SituationRecorder>.Instance);

        using var cancellation = new CancellationTokenSource(duration);
        await recorder.StartAsync(CancellationToken.None);

        // The recorder runs until cancelled; a short window is enough to capture the
        // snapshot plus whatever the test does while it is open.
        await Task.Delay(duration);
        await recorder.StopAsync(CancellationToken.None);
    }

    private async Task ReplayAsync(Situation.SituationClient client)
    {
        using var player = new ReplayPlayer(
            new GrpcSituationIngest(client),
            TestOptions(new ReplayOptions
            {
                Enabled = true,
                Path = RecordingPath,
                Speed = 1000,
                TickInterval = TimeSpan.FromMilliseconds(10)
            }),
            TimeProvider.System,
            NullLogger<ReplayPlayer>.Instance);

        using var cancellation = new CancellationTokenSource(E2E.Timeout);
        await player.StartAsync(cancellation.Token);

        var execute = player.ExecuteTask;
        Assert.NotNull(execute);
        await execute.WaitAsync(cancellation.Token);
    }

    private static IOptionsMonitor<T> TestOptions<T>(T value)
    {
        return new StaticOptionsMonitor<T>(value);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }
}
