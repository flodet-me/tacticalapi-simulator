using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Sources.Nws;

/// <summary>
///     Options for the US National Weather Service active-alerts source, bound
///     from "Simulator:Sources:Nws". The NWS API is free and keyless but
///     requires an identifying User-Agent header (set on the named HttpClient,
///     see NwsServiceCollectionExtensions) and is US-only, hence the state-code
///     <see cref="Area" /> filter rather than a bounding box.
/// </summary>
public sealed class NwsOptions() : TrackEmitterOptions(
    "GHGPGPO---****X", // illustrative hazard/installation marker; MIL-STD-2525C has no native weather symbology
    "SIM-NWS",
    TimeSpan.FromMinutes(15))
{
    public const string SectionName = SimulatorOptions.SourcesSectionName + ":Nws";

    /// <summary>Disabled by default so the simulator runs fully offline out of the box.</summary>
    public bool Enabled { get; set; }

    [Required] public Uri BaseAddress { get; set; } = new("https://api.weather.gov/");

    /// <summary>Two-letter US state/territory code (NWS "area" filter, e.g. "OK", "TX", "FL").</summary>
    [Required]
    [RegularExpression("^[A-Z]{2}$")]
    public string Area { get; set; } = "OK";

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Cap per poll to bound ingest cost (0 = unlimited).</summary>
    [Range(0, 10_000)]
    public int MaxAlertsPerPoll { get; set; } = 100;
}
