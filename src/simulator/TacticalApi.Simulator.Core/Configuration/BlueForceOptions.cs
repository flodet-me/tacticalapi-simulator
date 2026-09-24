using System.ComponentModel.DataAnnotations;

namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Settings for the <c>BlueForceTracking</c> service, bound from
///     "Simulator:BlueForce". Read through <c>IOptionsMonitor</c> like everything
///     else here, so a keep-alive timeout can be shortened underneath a running
///     client to watch it react.
/// </summary>
public sealed class BlueForceOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":BlueForce";

    /// <summary>
    ///     How long a blue force survives without a keep-alive before it is
    ///     implicitly deleted.
    ///     The contract requires a client to call <c>AddOrUpdateBlueForces</c> "at
    ///     least every 30s" and leaves the timeout itself to the application. The
    ///     default is twice that, not exactly 30s: a client honouring the contract
    ///     to the letter would otherwise be racing the sweeper on every cycle, and a
    ///     simulator that punished the documented cadence would be a trap rather
    ///     than a test target. Set it lower deliberately to exercise the timeout.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Interval at which timed-out blue forces are swept.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Identity (string form) of the blue force that represents this simulator
    ///     itself, flagged with <c>own_blue_force</c> on the wire. Null - the default -
    ///     means no blue force is flagged as own.
    ///     Server-side rather than taken from the update because <c>UpdateBlueForce</c>
    ///     has no such field: the contract makes "which one is me" a property of the
    ///     system answering, not of the report.
    /// </summary>
    public string? OwnIdentity { get; set; }

    /// <summary>
    ///     Hard cap on tracked blue forces; writes beyond it fail with an error
    ///     header, mirroring <c>Simulator:Performance:MaxSituationObjects</c>.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxBlueForces { get; set; } = 10_000;
}
