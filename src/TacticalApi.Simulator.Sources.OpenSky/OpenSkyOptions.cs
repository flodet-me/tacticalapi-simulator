using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.OpenSky;

/// <summary>
///     Options for the OpenSky Network live flight source, bound from
///     "Simulator:Sources:OpenSky". Anonymous OpenSky access is rate limited, so
///     keep PollInterval conservative (>= 10s recommended).
/// </summary>
public sealed class OpenSkyOptions() : TrackEmitterOptions(
    symbolCode: "SNAPCF---------", // neutral air, MIL-STD-2525C
    reporterId: "SIM-OPENSKY",
    trackTimeToLive: TimeSpan.FromMinutes(2))
{
    public const string SectionName = SimulatorOptions.SourcesSectionName + ":OpenSky";

    /// <summary>Disabled by default so the simulator runs fully offline out of the box.</summary>
    public bool Enabled { get; set; }

    [Required] public Uri BaseAddress { get; set; } = new("https://opensky-network.org/api/");

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Bounding box for the query (default: roughly Germany).</summary>
    [Range(-90, 90)]
    public double MinLatitude { get; set; } = 47.2;

    [Range(-90, 90)] public double MaxLatitude { get; set; } = 55.1;

    [Range(-180, 180)] public double MinLongitude { get; set; } = 5.8;

    [Range(-180, 180)] public double MaxLongitude { get; set; } = 15.1;

    /// <summary>Cap per poll to bound ingest cost (0 = unlimited).</summary>
    [Range(0, 100_000)]
    public int MaxTracksPerPoll { get; set; } = 500;
}
