using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Host.Web;

/// <summary>
///     Options for the read-only map GUI (<c>/ui</c>). Bound from "Simulator:MapUi" via
///     IOptionsMonitor (hot-reloadable) and served to the frontend via <c>/api/config</c>.
/// </summary>
public sealed class MapUiOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":MapUi";

    /// <summary>
    ///     Whether the GUI (<c>/ui</c>, <c>/api/objects</c>, <c>/api/config</c>) is served at all.
    ///     Enabled by default; checked per-request, so toggling it takes effect without a restart.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the frontend polls <c>/api/objects</c> for updates.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "00:01:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Initial map center latitude, used before any situation objects have loaded.</summary>
    [Range(-90, 90)]
    public double DefaultCenterLatitude { get; set; } = 53.08;

    /// <summary>Initial map center longitude; see <see cref="DefaultCenterLatitude" />.</summary>
    [Range(-180, 180)]
    public double DefaultCenterLongitude { get; set; } = 8.80;

    /// <summary>Initial zoom level (Leaflet zoom units).</summary>
    [Range(1, 19)]
    public int DefaultZoom { get; set; } = 9;
}
