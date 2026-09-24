using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Options for <see cref="GeometryShowcaseSource" />, the source that emits one object per
///     location kind a mapping client has to cope with. Unlike the scenario sources, nothing here
///     moves or is random: the shapes sit at fixed offsets around a center so a client's rendering
///     can be compared against the same picture on every run. Bound from
///     "Adapter:GeometryShowcase" via IOptionsMonitor (hot-reloadable).
/// </summary>
public sealed class GeometryShowcaseOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = AdapterOptions.SectionName + ":GeometryShowcase";

    /// <summary>Diagnostic name, kept in step with the section this binds to.</summary>
    public static string Name => SimulationSourceName.FromSectionName(SectionName);

    /// <summary>Whether the showcase runs; it is cheap enough (seven objects) to leave on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Delay between cycles. The shapes never change, so this only controls how often they are
    ///     re-reported - a correct implementation merges the repeats away.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "01:00:00")]
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Center the shapes are laid out around.</summary>
    [Range(-90, 90)]
    public double CenterLatitude { get; set; } = 53.08;

    /// <summary>Center the shapes are laid out around; see <see cref="CenterLatitude" />.</summary>
    [Range(-180, 180)]
    public double CenterLongitude { get; set; } = 8.8;

    /// <summary>Distance from the center to each shape's own slot, keeping the shapes apart.</summary>
    [Range(100, 100_000)]
    public double SpacingM { get; set; } = 2_000;

    /// <summary>Edge length/diameter of each shape - half of it is the ellipse's major axis.</summary>
    [Range(50, 50_000)]
    public double ShapeSizeM { get; set; } = 800;

    /// <summary>Reporter identity attached to every object this source emits.</summary>
    [Required]
    public string ReporterId { get; set; } = "SIM-SHAPES";
}
