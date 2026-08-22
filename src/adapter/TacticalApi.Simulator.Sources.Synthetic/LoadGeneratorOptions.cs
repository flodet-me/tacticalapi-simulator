using System.ComponentModel.DataAnnotations;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for <see cref="LoadGeneratorSource" />, the source that exists to push
///     an endpoint hard enough for the numbers on <c>/metrics</c> to mean something.
///     Distinct from <see cref="SyntheticAirTrackOptions" />, which simulates a
///     plausible air picture: this one makes no attempt at plausibility, it just
///     moves as many objects as you ask for as often as you ask for it.
/// </summary>
public sealed class LoadGeneratorOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":LoadGenerator";

    /// <summary>Diagnostic name, kept in step with the section this binds to.</summary>
    public static string Name => SimulationSourceName.FromSectionName(SectionName);

    /// <summary>Off by default; this is a deliberate stress tool, not a demo.</summary>
    public bool Enabled { get; set; }

    /// <summary>Size of the object population the generator maintains in the situation.</summary>
    [Range(1, 1_000_000)]
    public int ObjectCount { get; set; } = 10_000;

    /// <summary>
    ///     How many objects are reported per cycle. The generator walks the population
    ///     a window of this size at a time, so the population can be far larger than
    ///     what one AddOrUpdateSituationObjects message may carry
    ///     (Simulator:Performance:MaxReceiveMessageSizeMb on the receiving end) - and
    ///     this knob, not <see cref="ObjectCount" />, is the one that sets message size.
    /// </summary>
    [Range(1, 100_000)]
    public int BatchSize { get; set; } = 1_000;

    /// <summary>Delay between cycles. Combined with <see cref="BatchSize" />, this sets the offered rate.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Center latitude of the block of objects.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.08;

    /// <summary>Center longitude of the block of objects.</summary>
    [Range(-180, 180)]
    public double CenterLongitude { get; set; } = 8.80;

    /// <summary>Half-width of the square the objects are spread over, in degrees.</summary>
    [Range(0.001, 90.0)]
    public double SpreadDegrees { get; set; } = 1.0;

    /// <summary>Time-to-live stamped on each object; short values also exercise the expiry sweeper under load.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan TrackTimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reporter identity stamped on generated objects.</summary>
    [Required]
    [MinLength(1)]
    public string ReporterId { get; set; } = "SIM-LOAD";

    /// <summary>MIL-STD-2525 / APP-6 symbol code stamped on generated objects.</summary>
    [Required]
    [MinLength(1)]
    public string SymbolCode { get; set; } = "SUGP-----------";

    /// <summary>Symbol catalog the code above belongs to.</summary>
    public SymbolCatalog SymbolCatalog { get; set; } = SymbolCatalog.Mil2525C;
}
