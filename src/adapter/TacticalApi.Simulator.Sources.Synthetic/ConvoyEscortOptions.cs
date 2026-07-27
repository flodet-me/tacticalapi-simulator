using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for the convoy escort scenario: a logistics convoy shuttling back and forth along
///     a supply route through scripted high-risk zones (a culvert, a market chokepoint, ...)
///     where ambush probability is elevated. Bound from "Adapter:ConvoyEscort" via
///     IOptionsMonitor (hot-reloadable).
/// </summary>
public sealed class ConvoyEscortOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":ConvoyEscort";

    /// <summary>Disabled by default - opt-in scenario alongside the base SyntheticScenario.</summary>
    public bool Enabled { get; set; }

    /// <summary>Delay between simulation cycles.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>One end of the supply route.</summary>
    [Range(-90, 90)]
    public double StartLatitude { get; set; } = 52.92;

    /// <summary>One end of the supply route; see <see cref="StartLatitude" />.</summary>
    [Range(-180, 180)]
    public double StartLongitude { get; set; } = 8.55;

    /// <summary>The other end of the supply route.</summary>
    [Range(-90, 90)]
    public double EndLatitude { get; set; } = 53.20;

    /// <summary>The other end of the supply route; see <see cref="EndLatitude" />.</summary>
    [Range(-180, 180)]
    public double EndLongitude { get; set; } = 8.60;

    /// <summary>How long one leg of the route takes (the convoy then turns around and repeats).</summary>
    [Range(typeof(TimeSpan), "00:05:00", "1.00:00:00")]
    public TimeSpan TransitDuration { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Cargo trucks in the serial, not counting the gun trucks.</summary>
    [Range(1, 20)]
    public int CargoVehicleCount { get; set; } = 4;

    /// <summary>Armed escort vehicles (one leads, one trails).</summary>
    [Range(1, 8)]
    public int SecurityVehicleCount { get; set; } = 2;

    /// <summary>Personnel aboard each vehicle, for casualty accounting.</summary>
    [Range(1, 50)]
    public int PersonnelPerVehicle { get; set; } = 4;

    /// <summary>Ambush probability per cycle while away from any high-risk zone.</summary>
    [Range(0.0, 1.0)]
    public double BaseAmbushProbability { get; set; } = 0.01;

    /// <summary>Multiplier applied to <see cref="BaseAmbushProbability" /> within a risk zone.</summary>
    [Range(1.0, 200.0)]
    public double RiskZoneMultiplier { get; set; } = 20.0;

    /// <summary>Radius around a risk zone within which the multiplier applies.</summary>
    [Range(50, 5000)]
    public double RiskZoneRadiusM { get; set; } = 300;

    /// <summary>Chance a triggered ambush is IED-initiated rather than a pure small-arms contact.</summary>
    [Range(0.0, 1.0)]
    public double IedProbabilityGivenContact { get; set; } = 0.5;

    /// <summary>Minimum time between contacts, so one ambush doesn't immediately chain into another.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan ContactCooldown { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Deterministic seed for the risk-zone layout and contact rolls.</summary>
    public int Seed { get; set; } = 2024;

    /// <summary>Reporter identity attached to every object this source emits.</summary>
    [Required]
    public string ReporterId { get; set; } = "SIM-CONVOY";
}
