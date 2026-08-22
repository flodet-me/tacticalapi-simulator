using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     Turns any adapter into a recorder: with this enabled, every batch the
///     adapter's sources push is also appended to a ".jsonl" recording on the way
///     out (see <see cref="RecordingSituationIngest" />). It wraps the ingest client
///     rather than being a source of its own, so it captures whatever that adapter
///     produces - OpenSky, NWS, a scenario - with no per-source support needed and
///     no fidelity lost: what lands in the file is byte-for-byte what went on the
///     wire.
///     Bound from "Adapter:Recording", so it's an adapter-side concern like every
///     other setting under that root; the Host neither knows nor cares that a
///     recording is being made.
/// </summary>
public sealed class RecordingOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":Recording";

    /// <summary>
    ///     Whether outgoing writes are recorded. Off by default: recording is a
    ///     deliberate act, not something an adapter should start doing to your disk
    ///     because you ran it.
    ///     Unlike most options here this one is read once at startup - the recording
    ///     file is opened when the adapter starts, and silently swapping the sink
    ///     under a half-written recording would produce two useless files instead of
    ///     one good one.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Recording file path, relative to the adapter's working directory.
    ///     Truncated on start, so each run produces one self-contained recording
    ///     rather than an ever-growing concatenation of unrelated sessions.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Path { get; set; } = "recordings/recording.jsonl";
}
