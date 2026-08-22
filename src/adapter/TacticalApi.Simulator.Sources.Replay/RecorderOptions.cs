using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>
///     Options for the stream-side recorder (<see cref="SituationRecorder" />), which
///     subscribes to a TacticalAPI endpoint and records everything it sees.
///     This is the general-purpose capture path: it records whatever that endpoint's
///     situation is doing regardless of who is feeding it, including a third-party
///     implementation and clients you don't control. The lossless alternative, for
///     when the traffic is your own adapter's, is
///     <see cref="TacticalApi.Simulator.Core.Recording.RecordingOptions" /> - see this
///     project's README for which to reach for.
/// </summary>
public sealed class RecorderOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":Recorder";

    /// <summary>Diagnostic name, kept in step with the section this binds to.</summary>
    public static string Name => SimulationSourceName.FromSectionName(SectionName);

    /// <summary>Whether to record. Off by default - see <see cref="ReplayOptions.Enabled" />.</summary>
    public bool Enabled { get; set; }

    /// <summary>Recording file path, truncated on start.</summary>
    [Required]
    [MinLength(1)]
    public string Path { get; set; } = "recordings/recording.jsonl";

    /// <summary>
    ///     Whether the initial snapshot every subscription starts with is recorded as
    ///     the recording's first frame. On by default, so a replay reconstructs the
    ///     situation as it stood when recording began rather than starting empty and
    ///     only picking up whatever happened to change afterwards.
    /// </summary>
    public bool IncludeInitialSnapshot { get; set; } = true;
}
