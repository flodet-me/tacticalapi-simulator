using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>Options for <see cref="ReplayPlayer" />, which pushes a recording back into a TacticalAPI endpoint.</summary>
public sealed class ReplayOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":Replay";

    /// <summary>Diagnostic name, kept in step with the section this binds to.</summary>
    public static string Name => SimulationSourceName.FromSectionName(SectionName);

    /// <summary>
    ///     Whether to replay. Off by default: an adapter that started pushing a
    ///     recording at whatever endpoint it was pointed at, just because it was
    ///     started, would be a surprising default for a process that can also be run
    ///     purely to record.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Recording file to replay.</summary>
    [Required]
    [MinLength(1)]
    public string Path { get; set; } = "recordings/recording.jsonl";

    /// <summary>
    ///     Playback speed multiplier. 1.0 replays at the original pace; 10.0 packs ten
    ///     minutes of recording into one, which is what makes a recording usable as a
    ///     fast, deterministic test fixture rather than only as a demo.
    /// </summary>
    [Range(0.01, 1000.0)]
    public double Speed { get; set; } = 1.0;

    /// <summary>
    ///     Whether to start over once the last frame has been sent. Useful for a demo
    ///     that has to keep moving; off by default so a replay used as a test fixture
    ///     ends when the recording does.
    /// </summary>
    public bool Loop { get; set; }

    /// <summary>
    ///     How often the player wakes up to send whichever frames have come due. This
    ///     bounds replay timing accuracy: frames land on this grid rather than at
    ///     their exact recorded offsets.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Moves each replayed object's reporting_time to now, and shifts its
    ///     expiry_time by the same amount so it keeps the lifetime it was recorded
    ///     with. On by default, because a recording made yesterday carries yesterday's
    ///     timestamps: last-write-wins would reject the updates as stale, and anything
    ///     that did get through would expire immediately.
    ///     Turn it off to replay a recording exactly as captured - including its
    ///     timestamps - which is what you want when the thing under test IS the
    ///     server's staleness or expiry handling.
    /// </summary>
    public bool RestampReportingTime { get; set; } = true;
}
