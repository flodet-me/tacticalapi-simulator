using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Sources.Nws.Logging;

/// <summary>
///     Source-generated log messages for the NWS active-alerts source.
///     EventId range 3200-3299 - simulation sources share the 3XXX block (see also
///     Core: 1XXX, Host: 2XXX), one sub-block of 100 per source class.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 3210, EventName = "NoActiveAlerts", Level = LogLevel.Debug,
        Message = "NWS returned no active alerts for area {Area}")]
    public static partial void NoActiveAlerts(this ILogger logger, string area);

    [LoggerMessage(EventId = 3220, EventName = "AlertsProduced", Level = LogLevel.Debug,
        Message =
            "NWS produced {Count} update(s) for area {Area} ({Skipped} alert(s) skipped, {NoGeometry} without geometry)")]
    public static partial void AlertsProduced(this ILogger logger, int count, string area, int skipped,
        int noGeometry);

    [LoggerMessage(EventId = 3230, EventName = "AlertSkippedMissingFields", Level = LogLevel.Trace,
        Message = "Skipped alert: missing id or event name")]
    public static partial void AlertSkippedMissingFields(this ILogger logger);

    [LoggerMessage(EventId = 3240, EventName = "AlertCapReached", Level = LogLevel.Debug,
        Message = "MaxAlertsPerPoll cap of {Cap} reached; remaining alerts this poll were skipped")]
    public static partial void AlertCapReached(this ILogger logger, int cap);

    [LoggerMessage(EventId = 3250, EventName = "AlertHasNoGeometry", Level = LogLevel.Trace,
        Message = "Alert {AlertId} has no polygon geometry; only a TextDocument was emitted")]
    public static partial void AlertHasNoGeometry(this ILogger logger, string alertId);
}
