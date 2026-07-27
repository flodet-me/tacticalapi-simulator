using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Host.Logging;

/// <summary>
///     Source-generated log messages for the gRPC service layer.
///     EventId range 2000-2099 (see also Core: 1XXX, Sources.*: 3XXX), one block of 100
///     for SituationGrpcService, messages spaced by 10 to leave room for later additions.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 2010, EventName = "SubscriberConnected", Level = LogLevel.Information,
        Message = "Subscriber {Peer} connected")]
    public static partial void SubscriberConnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2020, EventName = "SubscriberDisconnected", Level = LogLevel.Information,
        Message = "Subscriber {Peer} disconnected")]
    public static partial void SubscriberDisconnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2030, EventName = "SnapshotBatchSent", Level = LogLevel.Trace,
        Message = "Sent initial snapshot batch of {Count} object(s) to {Peer}")]
    public static partial void SnapshotBatchSent(this ILogger logger, string peer, int count);

    [LoggerMessage(EventId = 2040, EventName = "EventBatchSent", Level = LogLevel.Trace,
        Message = "Sent live batch of {Count} object(s) to {Peer}")]
    public static partial void EventBatchSent(this ILogger logger, string peer, int count);

    [LoggerMessage(EventId = 2050, EventName = "GetSituationObjectsServed", Level = LogLevel.Trace,
        Message = "Served snapshot of {Count} object(s)")]
    public static partial void GetSituationObjectsServed(this ILogger logger, int count);

    [LoggerMessage(EventId = 2060, EventName = "AddOrUpdateReceived", Level = LogLevel.Trace,
        Message = "AddOrUpdateSituationObjects received {Count} object(s)")]
    public static partial void AddOrUpdateReceived(this ILogger logger, int count);

    [LoggerMessage(EventId = 2070, EventName = "AddOrUpdateFailed", Level = LogLevel.Warning,
        Message = "AddOrUpdateSituationObjects failed: {Error}")]
    public static partial void AddOrUpdateFailed(this ILogger logger, string? error);

    [LoggerMessage(EventId = 2080, EventName = "DeleteReceived", Level = LogLevel.Trace,
        Message = "DeleteSituationObjects received {Count} object(s)")]
    public static partial void DeleteReceived(this ILogger logger, int count);

    [LoggerMessage(EventId = 2090, EventName = "DeleteFailed", Level = LogLevel.Warning,
        Message = "DeleteSituationObjects failed: {Error}")]
    public static partial void DeleteFailed(this ILogger logger, string? error);
}
