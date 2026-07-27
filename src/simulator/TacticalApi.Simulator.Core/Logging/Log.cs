using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Core.Logging;

/// <summary>
///     Source-generated log messages for TacticalApi.Simulator.Core. Centralizing every
///     event here gives each one a stable EventId/EventName pair for filtering, and lets
///     the compiler check message templates against their arguments at build time.
///     EventId ranges (see also Host: 2XXX, Sources.*: 3XXX):
///     1000-1099 SimulationSourceRunner, 1100-1199 SituationStore, 1200-1299 ExpirySweeper.
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
}
