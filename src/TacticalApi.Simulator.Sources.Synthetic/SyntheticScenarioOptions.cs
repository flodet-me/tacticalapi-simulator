using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for the synthetic scenario source that exercises ALL situation
///     object types of the TacticalAPI. Bound from
///     "Simulator:Sources:SyntheticScenario" via IOptionsMonitor (hot-reloadable).
/// </summary>
public sealed class SyntheticScenarioOptions
{
    public const string SectionName = SimulatorOptions.SourcesSectionName + ":SyntheticScenario";

    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:00.500", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Scenario anchor point; the patrol route and all objects are laid out around it.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.08;

    [Range(-180, 180)] public double CenterLongitude { get; set; } = 8.80;

    /// <summary>Rough extent of the scenario in kilometers.</summary>
    [Range(0.5, 500)]
    public double ExtentKm { get; set; } = 20;

    /// <summary>Deterministic seed for event generation.</summary>
    public int Seed { get; set; } = 1337;

    /// <summary>Probability [0..1] per cycle that a new random action event is raised.</summary>
    [Range(0.0, 1.0)]
    public double EventProbability { get; set; } = 0.25;

    /// <summary>Lifetime of spontaneous action events before they expire.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan EventTimeToLive { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Probability [0..1] per cycle that a chat (TextDocument) message is sent.</summary>
    [Range(0.0, 1.0)]
    public double ChatProbability { get; set; } = 0.4;

    /// <summary>How long a full patrol lap around the route takes.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan PatrolLapDuration { get; set; } = TimeSpan.FromMinutes(10);

    [Required] public string ReporterId { get; set; } = "SIM-SCENARIO";
}
