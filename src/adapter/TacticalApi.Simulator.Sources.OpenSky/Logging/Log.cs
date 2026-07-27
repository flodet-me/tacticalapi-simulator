using Microsoft.Extensions.Logging;

namespace TacticalApi.Simulator.Sources.OpenSky.Logging;

/// <summary>
///     Source-generated log messages for the OpenSky live flight source.
///     EventId range 3100-3199 - simulation sources share the 3XXX block (see also
///     Core: 1XXX, Host: 2XXX), one sub-block of 100 per source class.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 3110, EventName = "NoStatesReturned", Level = LogLevel.Debug,
        Message = "OpenSky returned no states")]
    public static partial void NoStatesReturned(this ILogger logger);

    [LoggerMessage(EventId = 3120, EventName = "TracksProduced", Level = LogLevel.Debug,
        Message = "OpenSky produced {Count} track update(s) ({Skipped} state(s) skipped)")]
    public static partial void TracksProduced(this ILogger logger, int count, int skipped);

    [LoggerMessage(EventId = 3130, EventName = "StateSkipped", Level = LogLevel.Trace,
        Message = "Skipped state vector at index {Index}: missing icao24/latitude/longitude")]
    public static partial void StateSkipped(this ILogger logger, int index);

    [LoggerMessage(EventId = 3140, EventName = "TrackCapReached", Level = LogLevel.Debug,
        Message = "MaxTracksPerPoll cap of {Cap} reached; remaining states this poll were skipped")]
    public static partial void TrackCapReached(this ILogger logger, int cap);
}
