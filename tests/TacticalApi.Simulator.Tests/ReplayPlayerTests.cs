using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Sources.Replay;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="ReplayPlayer" />
///     (src/adapter/TacticalApi.Simulator.Sources.Replay/ReplayPlayer.cs).
/// </summary>
public sealed class ReplayPlayerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tacticalapi-replay-{Guid.NewGuid():N}");

    private string RecordingPath => Path.Combine(_directory, "recording.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task Replay_SendsEveryRecordedFrame()
    {
        // Arrange
        await WriteRecordingAsync(
            (TimeSpan.Zero, "track-1"),
            (TimeSpan.FromSeconds(1), "track-2"),
            (TimeSpan.FromSeconds(2), "track-3"));

        var ingest = new CapturingIngest();

        // Act - 1000x speed so the whole recording is due almost immediately.
        await RunAsync(ingest, new ReplayOptions
        {
            Enabled = true,
            Path = RecordingPath,
            Speed = 1000,
            TickInterval = TimeSpan.FromMilliseconds(10)
        });

        // Assert
        Assert.Equal(3, ingest.Updates.Count);
        Assert.Equal(
            ["track-1", "track-2", "track-3"],
            ingest.Updates.Select(u => u.Symbol.Identity.StringIdentity));
    }

    [Fact]
    public async Task Replay_RestampsReportingTimeToNow()
    {
        // Arrange - a recording carries the timestamps it was captured with; replayed
        // as-is, last-write-wins would reject them as stale.
        await WriteRecordingAsync((TimeSpan.Zero, "track-1"));
        var ingest = new CapturingIngest();

        // Act
        await RunAsync(ingest, new ReplayOptions
        {
            Enabled = true,
            Path = RecordingPath,
            Speed = 1000,
            TickInterval = TimeSpan.FromMilliseconds(10)
        });

        // Assert
        var replayed = ingest.Updates[0].Symbol.ReportingTime.ToDateTimeOffset();
        Assert.True(replayed > T0.AddYears(-1), "the recorded reporting_time was replayed unchanged");
        Assert.True(replayed <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Replay_ShiftsExpiryByTheSameAmountAsReportingTime()
    {
        // Arrange - expiry_time is an absolute instant, so an old recording's objects
        // would be swept the moment they arrived unless it moves with the report.
        // The lifetime the recording captured is what has to survive, not the
        // wall-clock instant it originally ended at.
        var recordedAt = DateTimeOffset.UtcNow.AddHours(-3);
        await WriteRecordingAsync(recordedAt, [(TimeSpan.Zero, "track-1")], TimeSpan.FromMinutes(10));
        var ingest = new CapturingIngest();

        // Act
        await RunAsync(ingest, new ReplayOptions
        {
            Enabled = true,
            Path = RecordingPath,
            Speed = 1000,
            TickInterval = TimeSpan.FromMilliseconds(10)
        });

        // Assert
        var symbol = ingest.Updates[0].Symbol;
        var lifetime = symbol.ExpiryTime.Content.ToDateTimeOffset() - symbol.ReportingTime.ToDateTimeOffset();
        Assert.InRange(lifetime, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
        Assert.True(symbol.ExpiryTime.Content.ToDateTimeOffset() > DateTimeOffset.UtcNow,
            "a replayed object expired before it was even sent");
    }

    [Fact]
    public async Task Replay_LeavesTimestampsAloneWhenRestampingIsOff()
    {
        // Arrange - the escape hatch for testing an implementation's own staleness
        // and expiry handling, where the recorded timestamps are the point.
        var recordedAt = DateTimeOffset.UtcNow.AddHours(-3);
        await WriteRecordingAsync(recordedAt, [(TimeSpan.Zero, "track-1")], TimeSpan.FromMinutes(10));
        var ingest = new CapturingIngest();

        // Act
        await RunAsync(ingest, new ReplayOptions
        {
            Enabled = true,
            Path = RecordingPath,
            Speed = 1000,
            TickInterval = TimeSpan.FromMilliseconds(10),
            RestampReportingTime = false
        });

        // Assert
        Assert.Equal(
            recordedAt.ToUnixTimeSeconds(),
            ingest.Updates[0].Symbol.ReportingTime.ToDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Replay_SendsDeletesAsDeletes()
    {
        // Arrange - a delete replayed as an update would resurrect the object.
        Directory.CreateDirectory(_directory);
        var delete = new DeleteSituationObject
        {
            Identity = new Identity { StringIdentity = "track-1" },
            Reporter = new Identity { StringIdentity = TestHelpers.TestReporterId },
            ReportingTime = Timestamp.FromDateTimeOffset(T0)
        };
        await File.WriteAllTextAsync(RecordingPath,
            RecordingFormat.Write(new RecordedFrame(1, TimeSpan.Zero, [], [delete])) + "\n");

        var ingest = new CapturingIngest();

        // Act
        await RunAsync(ingest, new ReplayOptions
        {
            Enabled = true,
            Path = RecordingPath,
            Speed = 1000,
            TickInterval = TimeSpan.FromMilliseconds(10)
        });

        // Assert
        Assert.Empty(ingest.Updates);
        Assert.Single(ingest.Deletes);
        Assert.Equal("track-1", ingest.Deletes[0].Identity.StringIdentity);
    }

    [Fact]
    public async Task Replay_DoesNothingWhenDisabled()
    {
        // Arrange
        await WriteRecordingAsync((TimeSpan.Zero, "track-1"));
        var ingest = new CapturingIngest();

        // Act
        await RunAsync(ingest, new ReplayOptions { Enabled = false, Path = RecordingPath });

        // Assert
        Assert.Empty(ingest.Updates);
    }

    private static async Task RunAsync(ISituationIngest ingest, ReplayOptions options)
    {
        using var player = new ReplayPlayer(
            ingest, TestHelpers.Options(options), TimeProvider.System, NullLogger<ReplayPlayer>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await player.StartAsync(cancellation.Token);

        // BackgroundService.ExecuteTask is set by StartAsync above; a disabled player
        // returns from ExecuteAsync immediately, which is a completed task, not null.
        var execute = player.ExecuteTask;
        Assert.NotNull(execute);
        await execute.WaitAsync(cancellation.Token);
    }

    private Task WriteRecordingAsync(params (TimeSpan Offset, string Id)[] frames)
    {
        return WriteRecordingAsync(T0, frames);
    }

    private async Task WriteRecordingAsync(
        DateTimeOffset recordedAt, (TimeSpan Offset, string Id)[] frames, TimeSpan? lifetime = null)
    {
        Directory.CreateDirectory(_directory);

        var lines = frames.Select((frame, index) => RecordingFormat.Write(new RecordedFrame(
            index + 1,
            frame.Offset,
            [
                TestHelpers.SymbolUpdate(
                    frame.Id, recordedAt, expiry: lifetime is null ? null : recordedAt + lifetime.Value)
            ],
            [])));

        await File.WriteAllTextAsync(RecordingPath, string.Join('\n', lines) + "\n");
    }

    private sealed class CapturingIngest : ISituationIngest
    {
        public List<UpdateSituationObject> Updates { get; } = [];
        public List<DeleteSituationObject> Deletes { get; } = [];

        public Task<IngestResult> AddOrUpdateAsync(
            IReadOnlyList<UpdateSituationObject> updates, CancellationToken cancellationToken = default)
        {
            Updates.AddRange(updates);
            return Task.FromResult(IngestResult.Ok);
        }

        public Task<IngestResult> DeleteAsync(
            IReadOnlyList<DeleteSituationObject> deletes, CancellationToken cancellationToken = default)
        {
            Deletes.AddRange(deletes);
            return Task.FromResult(IngestResult.Ok);
        }
    }
}
