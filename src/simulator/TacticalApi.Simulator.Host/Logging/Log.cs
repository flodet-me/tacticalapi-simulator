using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Host.Logging;

/// <summary>
///     Source-generated log messages for the gRPC service layer.
///     EventId ranges 2000-2099 SituationGrpcService, 2100-2199 fault injection,
///     2200-2299 control endpoints, 2300-2399 BlueForceTrackingGrpcService,
///     2400-2499 OwnPoseGrpcService (see also Core: 1XXX, Sources.*: 3XXX). Each class
///     gets a block of 100, messages spaced by 10 to leave room for later additions.
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

    // --- Fault injection (2100-2199) ---------------------------------------------------

    [LoggerMessage(EventId = 2110, EventName = "FaultInjected", Level = LogLevel.Warning,
        Message = "Injected '{Kind}' fault on {Method} (Simulator:Faults)")]
    public static partial void FaultInjected(this ILogger logger, string kind, string method);

    [LoggerMessage(EventId = 2120, EventName = "StreamAborted", Level = LogLevel.Warning,
        Message = "Aborted subscriber {Peer}'s stream after its injected lifetime elapsed")]
    public static partial void StreamAborted(this ILogger logger, string peer);

    // --- Control endpoints (2200-2299) -------------------------------------------------

    [LoggerMessage(EventId = 2210, EventName = "SituationReset", Level = LogLevel.Information,
        Message = "Control: reset dropped {Count} situation object(s)")]
    public static partial void SituationReset(this ILogger logger, int count);

    [LoggerMessage(EventId = 2220, EventName = "SituationPaused", Level = LogLevel.Information,
        Message = "Control: situation paused")]
    public static partial void SituationPaused(this ILogger logger);

    [LoggerMessage(EventId = 2230, EventName = "SituationResumed", Level = LogLevel.Information,
        Message = "Control: situation resumed")]
    public static partial void SituationResumed(this ILogger logger);

    [LoggerMessage(EventId = 2240, EventName = "ObjectsInjected", Level = LogLevel.Information,
        Message = "Control: injected {Count} situation object(s)")]
    public static partial void ObjectsInjected(this ILogger logger, int count);

    [LoggerMessage(EventId = 2250, EventName = "BlueForcesReset", Level = LogLevel.Information,
        Message = "Control: reset dropped {Count} blue force(s) and {Sources} position source(s)")]
    public static partial void BlueForcesReset(this ILogger logger, int count, int sources);

    // --- BlueForceTrackingGrpcService (2300-2399) --------------------------------------

    [LoggerMessage(EventId = 2310, EventName = "BlueForceSubscriberConnected", Level = LogLevel.Information,
        Message = "Blue force subscriber {Peer} connected")]
    public static partial void BlueForceSubscriberConnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2320, EventName = "BlueForceSubscriberDisconnected", Level = LogLevel.Information,
        Message = "Blue force subscriber {Peer} disconnected")]
    public static partial void BlueForceSubscriberDisconnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2330, EventName = "BlueForceBatchSent", Level = LogLevel.Trace,
        Message = "Sent batch of {Count} blue force(s) to {Peer}")]
    public static partial void BlueForceBatchSent(this ILogger logger, string peer, int count);

    [LoggerMessage(EventId = 2340, EventName = "GetBlueForcesServed", Level = LogLevel.Trace,
        Message = "Served snapshot of {Count} blue force(s)")]
    public static partial void GetBlueForcesServed(this ILogger logger, int count);

    [LoggerMessage(EventId = 2350, EventName = "AddOrUpdateBlueForcesReceived", Level = LogLevel.Trace,
        Message = "AddOrUpdateBlueForces received {Count} blue force(s)")]
    public static partial void AddOrUpdateBlueForcesReceived(this ILogger logger, int count);

    [LoggerMessage(EventId = 2360, EventName = "AddOrUpdateBlueForcesFailed", Level = LogLevel.Warning,
        Message = "AddOrUpdateBlueForces failed: {Error}")]
    public static partial void AddOrUpdateBlueForcesFailed(this ILogger logger, string? error);

    // --- OwnPoseGrpcService (2400-2499) ------------------------------------------------

    [LoggerMessage(EventId = 2410, EventName = "PositionSubscriberConnected", Level = LogLevel.Information,
        Message = "Position subscriber {Peer} connected")]
    public static partial void PositionSubscriberConnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2420, EventName = "PositionSubscriberDisconnected", Level = LogLevel.Information,
        Message = "Position subscriber {Peer} disconnected")]
    public static partial void PositionSubscriberDisconnected(this ILogger logger, string peer);

    [LoggerMessage(EventId = 2430, EventName = "GetPositionServed", Level = LogLevel.Trace,
        Message = "Served own position from source '{Source}'")]
    public static partial void GetPositionServed(this ILogger logger, string source);

    [LoggerMessage(EventId = 2440, EventName = "UpdatePositionReceived", Level = LogLevel.Trace,
        Message = "UpdatePosition received a fix from source '{Source}'")]
    public static partial void UpdatePositionReceived(this ILogger logger, string source);

    [LoggerMessage(EventId = 2450, EventName = "UpdatePositionFailed", Level = LogLevel.Warning,
        Message = "UpdatePosition failed: {Error}")]
    public static partial void UpdatePositionFailed(this ILogger logger, string? error);
}
