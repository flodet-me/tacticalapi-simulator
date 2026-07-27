using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for the combat outpost defense scenario: a static defended perimeter probed by a
///     persistent hostile cell whose activity follows a day/night cycle (real insurgent/irregular
///     activity skews heavily toward darkness) rather than a flat random rate. Bound from
///     "Adapter:CombatOutpostDefense" via IOptionsMonitor (hot-reloadable).
/// </summary>
public sealed class CombatOutpostDefenseOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":CombatOutpostDefense";

    /// <summary>Disabled by default - opt-in scenario alongside the base SyntheticScenario.</summary>
    public bool Enabled { get; set; }

    /// <summary>Delay between simulation cycles.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Center of the combat outpost's perimeter.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.00;

    /// <summary>Center of the combat outpost's perimeter; see <see cref="CenterLatitude" />.</summary>
    [Range(-180, 180)]
    public double CenterLongitude { get; set; } = 9.05;

    /// <summary>Radius of the defended perimeter.</summary>
    [Range(50, 2000)]
    public double PerimeterRadiusM { get; set; } = 250;

    /// <summary>Observation posts spaced evenly around the perimeter.</summary>
    [Range(1, 12)]
    public int ObservationPostCount { get; set; } = 4;

    /// <summary>Starting/maximum garrison strength (personnel), replaces losses slowly over time.</summary>
    [Range(1, 500)]
    public int GarrisonStrength { get; set; } = 40;

    /// <summary>Starting strength of the local hostile cell.</summary>
    [Range(1, 500)]
    public int InitialHostileCellStrength { get; set; } = 25;

    /// <summary>Baseline (daytime) contact probability per cycle.</summary>
    [Range(0.0, 1.0)]
    public double DayContactProbability { get; set; } = 0.02;

    /// <summary>Multiplier applied to <see cref="DayContactProbability" /> during night hours.</summary>
    [Range(1.0, 50.0)]
    public double NightContactProbabilityMultiplier { get; set; } = 5.0;

    /// <summary>UTC hour [0,23] night starts at.</summary>
    [Range(0, 23)]
    public int NightStartHourUtc { get; set; } = 19;

    /// <summary>UTC hour [0,23] night ends at (wraps past midnight if less than <see cref="NightStartHourUtc" />).</summary>
    [Range(0, 23)]
    public int NightEndHourUtc { get; set; } = 6;

    /// <summary>Given a contact, chance it's a full direct assault rather than probing/indirect fire.</summary>
    [Range(0.0, 1.0)]
    public double AssaultProbabilityGivenContact { get; set; } = 0.08;

    /// <summary>Given a contact that isn't an assault, chance it's indirect (mortar) fire rather than small-arms probing.</summary>
    [Range(0.0, 1.0)]
    public double IndirectFireProbabilityGivenContact { get; set; } = 0.4;

    /// <summary>Hostile cell strength regenerated per hour (reinforcement/recruitment), capped at the initial strength.</summary>
    [Range(0.0, 50.0)]
    public double HostileReinforcementPerHour { get; set; } = 0.6;

    /// <summary>Friendly personnel replaced per hour (medical return-to-duty/rotation), capped at <see cref="GarrisonStrength" />.</summary>
    [Range(0.0, 50.0)]
    public double GarrisonReplacementPerHour { get; set; } = 0.3;

    /// <summary>Minimum time between contacts.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "04:00:00")]
    public TimeSpan ContactCooldown { get; set; } = TimeSpan.FromMinutes(8);

    /// <summary>Deterministic seed for the perimeter/OP layout and contact rolls.</summary>
    public int Seed { get; set; } = 4077;

    /// <summary>Reporter identity attached to every object this source emits.</summary>
    [Required]
    public string ReporterId { get; set; } = "SIM-COP";
}
