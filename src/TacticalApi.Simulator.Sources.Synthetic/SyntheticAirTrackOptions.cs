using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for the offline synthetic air picture. Bound from
///     "Simulator:Sources:SyntheticAirTracks" and read via IOptionsMonitor, so
///     TrackCount / UpdateInterval / speeds can be changed while running.
/// </summary>
public sealed class SyntheticAirTrackOptions() : TrackEmitterOptions(
    "SFAPMF---------", // friendly air, MIL-STD-2525C
    "SIM-SYNTH-AIR",
    TimeSpan.FromSeconds(30))
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SourcesSectionName + ":SyntheticAirTracks";

    /// <summary>Enabled by default since this source needs no network access.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Delay between produced position updates.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Number of simulated aircraft to keep in orbit.</summary>
    [Range(1, 10_000)] public int TrackCount { get; set; } = 12;

    /// <summary>Center of the simulated orbit pattern.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.08;

    /// <summary>Center of the simulated orbit pattern; see <see cref="CenterLatitude" />.</summary>
    [Range(-180, 180)] public double CenterLongitude { get; set; } = 8.80;

    /// <summary>Orbit radius in kilometers.</summary>
    [Range(0.1, 2000)]
    public double RadiusKm { get; set; } = 60;

    /// <summary>Ground speed of each simulated aircraft.</summary>
    [Range(1, 3000)] public double SpeedMetersPerSecond { get; set; } = 180;

    /// <summary>Deterministic seed so the picture is reproducible.</summary>
    public int Seed { get; set; } = 42;
}
