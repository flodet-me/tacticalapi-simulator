using Microsoft.Extensions.Logging;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     Appends <see cref="RecordedFrame" />s to a ".jsonl" recording, stamping each
///     one with its offset from the first frame written.
///     Flushed after every frame: a recording's whole point is to survive whatever
///     killed the process that was making it, so buffering it away would defeat the
///     feature. Frames are cheap and infrequent (one per source cycle), so the cost
///     of that is irrelevant here.
/// </summary>
public sealed class RecordingWriter : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;
    private readonly StreamWriter _writer;
    private DateTimeOffset? _firstFrameAt;
    private long _sequence;

    /// <summary>Opens (creating directories as needed) the recording file for appending.</summary>
    public RecordingWriter(string path, TimeProvider time, ILogger logger)
    {
        Path = path;
        _time = time;
        _logger = logger;

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        _logger.RecordingStarted(path);
    }

    /// <summary>Number of frames written so far.</summary>
    public long FrameCount => Interlocked.Read(ref _sequence);

    /// <summary>Path of the recording being written.</summary>
    public string Path { get; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>
    ///     Writes one frame. The offset is derived from the time of the first frame,
    ///     so the recording always starts at +0ms however long the recorder idled
    ///     before anything happened.
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<UpdateSituationObject> updates,
        IReadOnlyList<DeleteSituationObject> deletes,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0 && deletes.Count == 0) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            _firstFrameAt ??= now;

            var frame = new RecordedFrame(
                Interlocked.Increment(ref _sequence), now - _firstFrameAt.Value, updates, deletes);

            await _writer.WriteLineAsync(RecordingFormat.Write(frame)).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.RecordingFrameWritten(frame.Sequence, frame.ObjectCount, (long)frame.Offset.TotalMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }
}
