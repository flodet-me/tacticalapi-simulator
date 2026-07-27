using System.ComponentModel.DataAnnotations;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Sources;

/// <summary>
///     Shared configuration for any source that emits tracks via
///     <see cref="TrackUpdateFactory.CreateSymbolUpdate" />: the symbol
///     code/catalog, the reporter identity, and how long a track survives
///     without a new report before it's marked expired. Derived options set
///     their own source-specific defaults in their constructor.
/// </summary>
public abstract class TrackEmitterOptions
{
    protected TrackEmitterOptions(string symbolCode, string reporterId, TimeSpan trackTimeToLive)
    {
        SymbolCode = symbolCode;
        ReporterId = reporterId;
        TrackTimeToLive = trackTimeToLive;
    }

    /// <summary>MIL-STD-2525 (or equivalent) symbol code stamped on every emitted track.</summary>
    [Required] public string SymbolCode { get; set; }

    /// <summary>Catalog <see cref="SymbolCode" /> is interpreted against.</summary>
    public SymbolCatalog SymbolCatalog { get; set; } = SymbolCatalog.Mil2525C;

    /// <summary>How long a track survives without a new report before it expires.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan TrackTimeToLive { get; set; }

    /// <summary>Reporter identity attached to every track this source emits.</summary>
    [Required] public string ReporterId { get; set; }
}
