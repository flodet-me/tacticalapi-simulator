using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.OpenSky;

/// <summary>
///     Options for the OpenSky Network live flight source, bound from
///     "Adapter:OpenSky". Anonymous OpenSky access is rate limited, so
///     keep PollInterval conservative (>= 10s recommended).
/// </summary>
public sealed class OpenSkyOptions() : TrackEmitterOptions(
    "SNAPCF---------", // neutral air, MIL-STD-2525C
    "SIM-OPENSKY",
    TimeSpan.FromMinutes(2))
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":OpenSky";

    /// <summary>Disabled by default so the simulator runs fully offline out of the box.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the OpenSky REST API.</summary>
    [Required] public Uri BaseAddress { get; set; } = new("https://opensky-network.org/api/");

    /// <summary>How often to poll for state vectors.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Bounding box for the query (default: roughly Germany).</summary>
    [Range(-90, 90)]
    public double MinLatitude { get; set; } = 47.2;

    /// <summary>Bounding box northern edge; see <see cref="MinLatitude" />.</summary>
    [Range(-90, 90)] public double MaxLatitude { get; set; } = 55.1;

    /// <summary>Bounding box western edge; see <see cref="MinLatitude" />.</summary>
    [Range(-180, 180)] public double MinLongitude { get; set; } = 5.8;

    /// <summary>Bounding box eastern edge; see <see cref="MinLatitude" />.</summary>
    [Range(-180, 180)] public double MaxLongitude { get; set; } = 15.1;

    /// <summary>Cap per poll to bound ingest cost (0 = unlimited).</summary>
    [Range(0, 100_000)]
    public int MaxTracksPerPoll { get; set; } = 500;
}
