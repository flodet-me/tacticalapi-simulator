using System.ComponentModel.DataAnnotations;

namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Root options for the simulator, bound from the "Simulator" configuration
///     section - covering all three services of the contract (Situation,
///     BlueForceTracking, OwnPose). Consumed via <c>IOptionsMonitor&lt;SimulatorOptions&gt;</c> so
///     edits to appsettings.json apply at runtime without a restart.
/// </summary>
public sealed class SimulatorOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = "Simulator";

    /// <summary>
    ///     Reporter identity the simulator itself uses (expiry sweeps etc.).
    ///     The TacticalAPI docs suggest "TacticalAPI" when in doubt.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string ReporterId { get; set; } = "TacticalAPI-Simulator";

    /// <summary>Interval at which expired objects are marked as deleted.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExpirySweepInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Throughput/capacity tuning knobs; see <see cref="PerformanceOptions" />.</summary>
    public PerformanceOptions Performance { get; set; } = new();

    /// <summary>Blue force tracking settings; see <see cref="BlueForceOptions" />.</summary>
    public BlueForceOptions BlueForce { get; set; } = new();

    /// <summary>Own position settings; see <see cref="OwnPoseOptions" />.</summary>
    public OwnPoseOptions OwnPose { get; set; } = new();
}
