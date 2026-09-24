using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for the blue force patrol scenario: a dismounted section, its
///     carrier vehicle and a small UAS, all reporting themselves over
///     <c>BlueForceTracking</c> while the section leader's GNSS feeds
///     <c>OwnPose</c>. Bound from "Adapter:BlueForcePatrol" via IOptionsMonitor
///     (hot-reloadable).
/// </summary>
public sealed class BlueForcePatrolOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":BlueForcePatrol";

    /// <summary>
    ///     Enabled by default: it is fully offline and it is the one source that
    ///     puts anything at all behind the BlueForceTracking and OwnPose services,
    ///     so running the Host plus this adapter should show all three services
    ///     alive without further configuration.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Delay between keep-alive cycles. The call is itself the keep-alive, so
    ///     this must stay comfortably below the implementation's timeout - the
    ///     contract asks for at least every 30s and the default here is six times
    ///     more often, which leaves a blue force alive across several lost calls.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "00:00:30")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Center of the patrol area.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.08;

    /// <summary>Center of the patrol area; see <see cref="CenterLatitude" />.</summary>
    [Range(-180, 180)]
    public double CenterLongitude { get; set; } = 8.8;

    /// <summary>Radius of the vehicle's patrol loop.</summary>
    [Range(100, 50_000)]
    public double PatrolRadiusM { get; set; } = 1200;

    /// <summary>Time the carrier vehicle takes for one full lap of the loop.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan LapDuration { get; set; } = TimeSpan.FromMinutes(12);

    /// <summary>Riflemen in the section, not counting the leader.</summary>
    [Range(1, 30)]
    public int DismountCount { get; set; } = 3;

    /// <summary>
    ///     How long the section stays mounted, then dismounted, on repeat. Mounting
    ///     is what exercises <c>mount_host</c>: while mounted every dismount reports
    ///     the vehicle as its host and rides its position.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan MountedPhaseDuration { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How long the section stays dismounted before remounting.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan DismountedPhaseDuration { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How far the dismounts spread from the vehicle once off it.</summary>
    [Range(10, 2000)]
    public double DismountSpreadM { get; set; } = 120;

    /// <summary>Identifier of the position source the section leader's device reports as.</summary>
    [Required]
    [MinLength(1)]
    public string PositionSourceIdentifier { get; set; } = "GNSS";

    /// <summary>
    ///     Chance per cycle that the leader's GNSS drops out - the contract's own
    ///     example of an expiring position, "because the user entered a building".
    ///     While out, the source reports no position at all and the implementation's
    ///     own staleness handling is what a client sees.
    /// </summary>
    [Range(0.0, 1.0)]
    public double GnssOutageProbability { get; set; } = 0.05;

    /// <summary>How long a GNSS outage lasts once it starts.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan GnssOutageDuration { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Deterministic seed for the dismount layout and GNSS outage rolls.</summary>
    public int Seed { get; set; } = 7;

    /// <summary>Callsign prefix every member of the section reports under.</summary>
    [Required]
    [MinLength(1)]
    public string Callsign { get; set; } = "BADGER";
}
