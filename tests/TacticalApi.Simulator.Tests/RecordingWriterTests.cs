using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Recording;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for the recording writer/reader pair and the ingest decorator that
///     drives them (src/simulator/TacticalApi.Simulator.Core/Recording/).
/// </summary>
public sealed class RecordingWriterTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tacticalapi-rec-{Guid.NewGuid():N}");

    private string Path0 => Path.Combine(_directory, "recording.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task WriteAsync_StampsOffsetsRelativeToTheFirstFrame()
    {
        // Arrange - offsets are relative so a recording replays identically whenever
        // it is started, however long the recorder idled before anything happened.
        var time = new TestHelpers.MutableTimeProvider(T0.AddMinutes(37));
        await using var writer = new RecordingWriter(Path0, time, NullLogger.Instance);

        // Act
        await writer.WriteAsync([TestHelpers.SymbolUpdate("track-1", T0)], []);
        time.Advance(TimeSpan.FromSeconds(5));
        await writer.WriteAsync([TestHelpers.SymbolUpdate("track-2", T0)], []);

        // Assert
        var frames = await RecordingReader.ReadAllAsync(Path0, NullLogger.Instance);
        Assert.Equal(2, frames.Count);
        Assert.Equal(TimeSpan.Zero, frames[0].Offset);
        Assert.Equal(TimeSpan.FromSeconds(5), frames[1].Offset);
        Assert.Equal(1, frames[0].Sequence);
        Assert.Equal(2, frames[1].Sequence);
    }

    [Fact]
    public async Task WriteAsync_SkipsEmptyBatches()
    {
        // Arrange
        var time = new TestHelpers.MutableTimeProvider(T0);
        await using var writer = new RecordingWriter(Path0, time, NullLogger.Instance);

        // Act
        await writer.WriteAsync([], []);

        // Assert
        Assert.Equal(0, writer.FrameCount);
    }

    [Fact]
    public async Task WriteAsync_CreatesMissingDirectories()
    {
        // Arrange - the default path is under "recordings/", which won't exist on a
        // fresh checkout.
        var nested = Path.Combine(_directory, "a", "b", "recording.jsonl");

        // Act
        await using (var writer = new RecordingWriter(nested, new TestHelpers.MutableTimeProvider(T0), NullLogger.Instance))
        {
            await writer.WriteAsync([TestHelpers.SymbolUpdate("track-1", T0)], []);
        }

        // Assert
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task ReadAllAsync_SkipsATruncatedFinalLine()
    {
        // Arrange - the normal shape of a recording whose process was killed.
        Directory.CreateDirectory(_directory);
        var good = RecordingFormat.Write(
            new RecordedFrame(1, TimeSpan.Zero, [TestHelpers.SymbolUpdate("track-1", T0)], []));
        await File.WriteAllTextAsync(Path0, good + "\n" + good[..(good.Length / 2)]);

        // Act
        var frames = await RecordingReader.ReadAllAsync(Path0, NullLogger.Instance);

        // Assert
        Assert.Single(frames);
    }

    [Fact]
    public async Task RecordingIngest_RecordsWhatItForwards()
    {
        // Arrange - recording on the way out is what makes it lossless: the frame
        // written is the very message that went to the inner ingest.
        var inner = new CapturingIngest();
        await using var ingest = new RecordingSituationIngest(
            inner,
            Options.Create(new RecordingOptions { Enabled = true, Path = Path0 }),
            new TestHelpers.MutableTimeProvider(T0),
            NullLogger<RecordingSituationIngest>.Instance);

        // Act
        await ingest.AddOrUpdateAsync([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA")]);
        await ingest.DeleteAsync([TestHelpers.Delete("track-1", T0.AddMinutes(1))]);
        await ingest.DisposeAsync();

        // Assert - forwarded unchanged...
        Assert.Single(inner.Updates);
        Assert.Single(inner.Deletes);

        // ...and recorded identically.
        var frames = await RecordingReader.ReadAllAsync(Path0, NullLogger.Instance);
        Assert.Equal(2, frames.Count);
        Assert.Equal("ALPHA", frames[0].Updates[0].Symbol.Name.Content);
        Assert.Equal("track-1", frames[1].Deletes[0].Identity.StringIdentity);
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
