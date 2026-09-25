using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Core.Logging;

/// <summary>
///     Source-generated log messages for TacticalApi.Simulator.Core. Centralizing every
///     event here gives each one a stable EventId/EventName pair for filtering, and lets
///     the compiler check message templates against their arguments at build time.
///     EventId ranges (see also Host: 2XXX, Sources.*: 3XXX):
///     1000-1099 SimulationSourceRunner, 1100-1199 SituationStore, 1200-1299 ExpirySweeper,
///     1300-1399 recording/replay, 1400-1499 BlueForceStore + its sweeper,
///     1500-1599 OwnPoseStore + its sweeper.
///     Each class gets a block of 100, each message a multiple of 10 within it, leaving
///     room to insert new events later without renumbering existing ones.
/// </summary>
internal static partial class Log
{
    // --- SimulationSourceRunner (1000-1099) ------------------------------------------

    [LoggerMessage(EventId = 1010, EventName = "RunnerStarted", Level = LogLevel.Information,
        Message = "Simulation source '{Source}' runner started")]
    public static partial void RunnerStarted(this ILogger logger, string source);

    [LoggerMessage(EventId = 1020, EventName = "SourceDisabled", Level = LogLevel.Debug,
        Message = "Source '{Source}' disabled; polling suspended")]
    public static partial void SourceDisabled(this ILogger logger, string source);

    [LoggerMessage(EventId = 1030, EventName = "SourceEnabled", Level = LogLevel.Information,
        Message = "Source '{Source}' enabled; resuming polling")]
    public static partial void SourceEnabled(this ILogger logger, string source);

    [LoggerMessage(EventId = 1040, EventName = "CycleProduced", Level = LogLevel.Trace,
        Message = "Source '{Source}' cycle {Cycle} produced {Count} update(s) in {ElapsedMs:F1}ms")]
    public static partial void CycleProduced(this ILogger logger, string source, long cycle, int count,
        double elapsedMs);

    [LoggerMessage(EventId = 1050, EventName = "IngestFailed", Level = LogLevel.Warning,
        Message = "Source '{Source}' ingest failed: {Error}")]
    public static partial void IngestFailed(this ILogger logger, string source, string? error);

    [LoggerMessage(EventId = 1060, EventName = "ProduceFailed", Level = LogLevel.Error,
        Message = "Source '{Source}' failed; retrying next cycle")]
    public static partial void ProduceFailed(this ILogger logger, Exception exception, string source);

    // --- SituationStore (1100-1199) ---------------------------------------------------

    [LoggerMessage(EventId = 1110, EventName = "UpdateIgnoredStale", Level = LogLevel.Debug,
        Message = "Ignoring stale update for {Key}")]
    public static partial void UpdateIgnoredStale(this ILogger logger, string key);

    [LoggerMessage(EventId = 1120, EventName = "ObjectLimitReached", Level = LogLevel.Warning,
        Message = "Object limit of {MaxObjects} reached; rejecting new object {Key}")]
    public static partial void ObjectLimitReached(this ILogger logger, int maxObjects, string key);

    [LoggerMessage(EventId = 1130, EventName = "UnsupportedType", Level = LogLevel.Warning,
        Message = "Rejected update: situation object type '{TypeCase}' has no registered merger")]
    public static partial void UnsupportedType(this ILogger logger, string typeCase);

    [LoggerMessage(EventId = 1140, EventName = "MissingIdentity", Level = LogLevel.Warning,
        Message = "Rejected update: missing required identity")]
    public static partial void MissingIdentity(this ILogger logger);

    [LoggerMessage(EventId = 1150, EventName = "MissingReportingTime", Level = LogLevel.Warning,
        Message = "Rejected update for {Key}: missing required reporting_time")]
    public static partial void MissingReportingTime(this ILogger logger, string key);

    [LoggerMessage(EventId = 1160, EventName = "BatchProcessed", Level = LogLevel.Trace,
        Message = "AddOrUpdate processed {Total} update(s): {Applied} applied, {Stale} stale/ignored")]
    public static partial void BatchProcessed(this ILogger logger, int total, int applied, int stale);

    [LoggerMessage(EventId = 1170, EventName = "ObjectsDeleted", Level = LogLevel.Trace,
        Message = "Delete processed {Total} request(s): {Applied} applied")]
    public static partial void ObjectsDeleted(this ILogger logger, int total, int applied);

    [LoggerMessage(EventId = 1180, EventName = "WriteRejectedWhilePaused", Level = LogLevel.Debug,
        Message = "Rejected a write batch of {Count} object(s): the simulator is paused")]
    public static partial void WriteRejectedWhilePaused(this ILogger logger, int count);

    [LoggerMessage(EventId = 1190, EventName = "StoreCleared", Level = LogLevel.Information,
        Message = "Situation reset: dropped {Count} situation object(s)")]
    public static partial void StoreCleared(this ILogger logger, int count);

    // --- ExpirySweeper (1200-1299) -----------------------------------------------------

    [LoggerMessage(EventId = 1210, EventName = "SweepCompleted", Level = LogLevel.Information,
        Message = "Marked {Count} expired situation object(s) as deleted")]
    public static partial void SweepCompleted(this ILogger logger, int count);

    [LoggerMessage(EventId = 1220, EventName = "SweepNoExpired", Level = LogLevel.Trace,
        Message = "Expiry sweep completed; no expired objects found")]
    public static partial void SweepNoExpired(this ILogger logger);

    [LoggerMessage(EventId = 1230, EventName = "SweepFailed", Level = LogLevel.Error,
        Message = "Expiry sweep failed; retrying next interval")]
    public static partial void SweepFailed(this ILogger logger, Exception exception);

    // --- Recording / replay (1300-1399) ------------------------------------------------

    [LoggerMessage(EventId = 1310, EventName = "RecordingStarted", Level = LogLevel.Information,
        Message = "Recording situation traffic to '{Path}'")]
    public static partial void RecordingStarted(this ILogger logger, string path);

    [LoggerMessage(EventId = 1320, EventName = "RecordingFrameWritten", Level = LogLevel.Trace,
        Message = "Recorded frame {Sequence} ({Count} object(s)) at +{OffsetMs}ms")]
    public static partial void RecordingFrameWritten(this ILogger logger, long sequence, int count, long offsetMs);

    [LoggerMessage(EventId = 1330, EventName = "RecordingFailed", Level = LogLevel.Error,
        Message = "Recording to '{Path}' failed; the recorder will stop")]
    public static partial void RecordingFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1340, EventName = "RecordingFrameSkipped", Level = LogLevel.Warning,
        Message = "Skipped an unreadable frame at line {Line} of '{Path}'")]
    public static partial void RecordingFrameSkipped(this ILogger logger, int line, string path);

    [LoggerMessage(EventId = 1350, EventName = "RecordingClosed", Level = LogLevel.Information,
        Message = "Recording '{Path}' closed after {Frames} frame(s)")]
    public static partial void RecordingClosed(this ILogger logger, string path, long frames);

    // --- BlueForceStore / BlueForceTimeoutSweeper (1400-1499) --------------------------

    [LoggerMessage(EventId = 1410, EventName = "BlueForceIgnoredStale", Level = LogLevel.Debug,
        Message = "Ignoring stale blue force update for {Key}")]
    public static partial void BlueForceIgnoredStale(this ILogger logger, string key);

    [LoggerMessage(EventId = 1420, EventName = "BlueForceLimitReached", Level = LogLevel.Warning,
        Message = "Blue force limit of {MaxBlueForces} reached; rejecting new blue force {Key}")]
    public static partial void BlueForceLimitReached(this ILogger logger, int maxBlueForces, string key);

    [LoggerMessage(EventId = 1430, EventName = "BlueForceMissingContactTime", Level = LogLevel.Warning,
        Message = "Rejected blue force update for {Key}: missing required last_contact_time")]
    public static partial void BlueForceMissingContactTime(this ILogger logger, string key);

    [LoggerMessage(EventId = 1440, EventName = "BlueForceBatchProcessed", Level = LogLevel.Trace,
        Message = "AddOrUpdateBlueForces processed {Total} update(s): {Applied} applied, {Stale} stale/ignored")]
    public static partial void BlueForceBatchProcessed(this ILogger logger, int total, int applied, int stale);

    [LoggerMessage(EventId = 1450, EventName = "BlueForceWriteRejectedWhilePaused", Level = LogLevel.Debug,
        Message = "Rejected a blue force batch of {Count} update(s): the simulator is paused")]
    public static partial void BlueForceWriteRejectedWhilePaused(this ILogger logger, int count);

    [LoggerMessage(EventId = 1460, EventName = "BlueForcesTimedOut", Level = LogLevel.Information,
        Message = "Implicitly deleted {Count} blue force(s) after their keep-alive timeout elapsed")]
    public static partial void BlueForcesTimedOut(this ILogger logger, int count);

    [LoggerMessage(EventId = 1470, EventName = "BlueForceStoreCleared", Level = LogLevel.Information,
        Message = "Blue force reset: dropped {Count} blue force(s)")]
    public static partial void BlueForceStoreCleared(this ILogger logger, int count);

    [LoggerMessage(EventId = 1480, EventName = "BlueForceSweepFailed", Level = LogLevel.Error,
        Message = "Blue force timeout sweep failed; retrying next interval")]
    public static partial void BlueForceSweepFailed(this ILogger logger, Exception exception);

    // --- OwnPoseStore / OwnPoseStalenessSweeper (1500-1599) ---------------------------

    [LoggerMessage(EventId = 1510, EventName = "PositionUpdated", Level = LogLevel.Trace,
        Message = "Position source '{Source}' reported a new fix")]
    public static partial void PositionUpdated(this ILogger logger, string source);

    [LoggerMessage(EventId = 1520, EventName = "PositionMissingSource", Level = LogLevel.Warning,
        Message = "Rejected position update: missing required source_identifier")]
    public static partial void PositionMissingSource(this ILogger logger);

    [LoggerMessage(EventId = 1530, EventName = "PositionRejectedWhilePaused", Level = LogLevel.Debug,
        Message = "Rejected a position update: the simulator is paused")]
    public static partial void PositionRejectedWhilePaused(this ILogger logger);

    [LoggerMessage(EventId = 1540, EventName = "PositionExpired", Level = LogLevel.Information,
        Message = "Primary position from '{Source}' changed validity and was announced")]
    public static partial void PositionExpired(this ILogger logger, string source);

    [LoggerMessage(EventId = 1550, EventName = "OwnPoseCleared", Level = LogLevel.Information,
        Message = "Own pose reset: dropped {Count} position source(s)")]
    public static partial void OwnPoseCleared(this ILogger logger, int count);

    [LoggerMessage(EventId = 1560, EventName = "OwnPoseSweepFailed", Level = LogLevel.Error,
        Message = "Own pose staleness sweep failed; retrying next interval")]
    public static partial void OwnPoseSweepFailed(this ILogger logger, Exception exception);
}
