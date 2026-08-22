using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Sources.Replay.Logging;

/// <summary>
///     Source-generated log messages for the recorder and the replay player.
///     EventId range 3500-3599 (see also Core: 1XXX, Host: 2XXX, other Sources.*:
///     3100-3499), messages spaced by 10 to leave room for later additions.
/// </summary>
internal static partial class Log
{
    // --- SituationRecorder (3500-3549) -------------------------------------------------

    [LoggerMessage(EventId = 3510, EventName = "RecorderSubscribed", Level = LogLevel.Information,
        Message = "Recorder subscribed to {Address}; recording to '{Path}'")]
    public static partial void RecorderSubscribed(this ILogger logger, Uri address, string path);

    [LoggerMessage(EventId = 3520, EventName = "RecorderSnapshotSkipped", Level = LogLevel.Information,
        Message = "Skipped the initial snapshot of {Count} object(s) (IncludeInitialSnapshot is false)")]
    public static partial void RecorderSnapshotSkipped(this ILogger logger, int count);

    [LoggerMessage(EventId = 3530, EventName = "RecorderStreamFailed", Level = LogLevel.Error,
        Message = "Recorder's subscription to {Address} failed; retrying in {DelaySeconds}s")]
    public static partial void RecorderStreamFailed(this ILogger logger, Exception exception, Uri address,
        double delaySeconds);

    [LoggerMessage(EventId = 3540, EventName = "RecorderStopped", Level = LogLevel.Information,
        Message = "Recorder stopped after {Frames} frame(s) covering {Objects} object(s)")]
    public static partial void RecorderStopped(this ILogger logger, long frames, long objects);

    // --- ReplayPlayer (3550-3599) ------------------------------------------------------

    [LoggerMessage(EventId = 3550, EventName = "ReplayStarted", Level = LogLevel.Information,
        Message = "Replaying {Frames} frame(s) from '{Path}' at {Speed}x (recording length {LengthSeconds:F1}s)")]
    public static partial void ReplayStarted(this ILogger logger, int frames, string path, double speed,
        double lengthSeconds);

    [LoggerMessage(EventId = 3560, EventName = "ReplayFileMissing", Level = LogLevel.Warning,
        Message = "Replay file '{Path}' does not exist; waiting for it to appear")]
    public static partial void ReplayFileMissing(this ILogger logger, string path);

    [LoggerMessage(EventId = 3570, EventName = "ReplayFrameSent", Level = LogLevel.Trace,
        Message = "Replayed frame {Sequence} ({Updates} update(s), {Deletes} delete(s))")]
    public static partial void ReplayFrameSent(this ILogger logger, long sequence, int updates, int deletes);

    [LoggerMessage(EventId = 3580, EventName = "ReplayIngestFailed", Level = LogLevel.Warning,
        Message = "Replay of frame {Sequence} was rejected by the endpoint: {Error}")]
    public static partial void ReplayIngestFailed(this ILogger logger, long sequence, string? error);

    [LoggerMessage(EventId = 3590, EventName = "ReplayFinished", Level = LogLevel.Information,
        Message = "Replay of '{Path}' finished after {Frames} frame(s); looping: {Looping}")]
    public static partial void ReplayFinished(this ILogger logger, string path, long frames, bool looping);
}
