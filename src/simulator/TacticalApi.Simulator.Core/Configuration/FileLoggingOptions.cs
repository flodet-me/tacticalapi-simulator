using System.ComponentModel.DataAnnotations;

namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Options for the optional rolling-file log sink, common to every executable
///     (Host and every <c>Adapter.*</c>). Bound from "Logging:File" - a sibling of
///     the standard "Logging:LogLevel" section, not nested under "Simulator" or
///     "Adapter", since it's plumbing for the logging system itself rather than
///     one executable's own behavior. Read once at startup (see
///     <see cref="TacticalApi.Simulator.Core.Logging.FileLoggingExtensions.AddFileLogging" />) rather than via
///     <c>IOptionsMonitor</c> like every other option here: the log provider is
///     wired into the host builder before the DI container exists, and swapping a
///     provider out at runtime isn't something the logging pipeline supports -
///     toggling this requires a restart, unlike the rest of this simulator's
///     hot-reloadable config.
/// </summary>
public sealed class FileLoggingOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = "Logging:File";

    /// <summary>Disabled by default; console logging alone is enough for local runs.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Rolling file path. Written as newline-delimited JSON (one structured log
    ///     event per line, see <see cref="TacticalApi.Simulator.Core.Logging.FileLoggingExtensions.AddFileLogging" />),
    ///     hence the ".jsonl" default rather than ".log". Serilog inserts the date
    ///     before the extension per <see cref="Serilog.RollingInterval.Day" /> (e.g.
    ///     "logs/host-.jsonl" -&gt; "logs/host-20260415.jsonl"), relative to the
    ///     executable's working directory.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Path { get; set; } = "logs/log-.jsonl";
}
