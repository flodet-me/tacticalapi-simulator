using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic.Logging;

/// <summary>
///     Source-generated log messages for the offline synthetic simulation sources.
///     EventId range 3300-3499 - simulation sources share the 3XXX block (see also
///     Core: 1XXX, Host: 2XXX): 3300-3399 SyntheticAirTrackSource, 3400-3499
///     SyntheticScenarioSource.
/// </summary>
internal static partial class Log
{
    // --- SyntheticAirTrackSource (3300-3399) -----------------------------------------

    [LoggerMessage(EventId = 3310, EventName = "AirTracksProduced", Level = LogLevel.Trace,
        Message = "Synthetic air-track cycle produced {Count} track update(s)")]
    public static partial void AirTracksProduced(this ILogger logger, int count);

    // --- SyntheticScenarioSource (3400-3499) -----------------------------------------

    [LoggerMessage(EventId = 3410, EventName = "ScenarioCycleProduced", Level = LogLevel.Trace,
        Message = "Synthetic scenario cycle produced {Count} update(s)")]
    public static partial void ScenarioCycleProduced(this ILogger logger, int count);

    [LoggerMessage(EventId = 3420, EventName = "IncidentRaised", Level = LogLevel.Debug,
        Message = "Synthetic scenario raised incident {IncidentId}: {IncidentName} (threat level {ThreatLevel})")]
    public static partial void IncidentRaised(this ILogger logger, string incidentId, string incidentName,
        int threatLevel);

    [LoggerMessage(EventId = 3430, EventName = "ChatMessageSent", Level = LogLevel.Trace,
        Message = "Synthetic scenario sent chat message {ChatId}: {Text}")]
    public static partial void ChatMessageSent(this ILogger logger, string chatId, string text);

    [LoggerMessage(EventId = 3440, EventName = "PatrolTaskStatusChanged", Level = LogLevel.Debug,
        Message = "Synthetic scenario patrol task status changed to {Status}")]
    public static partial void PatrolTaskStatusChanged(this ILogger logger, string status);
}
