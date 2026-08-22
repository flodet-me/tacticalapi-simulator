using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Host.Control;

/// <summary>
///     Options for the control endpoints (<c>/api/control/*</c>), bound from
///     "Simulator:Control" and checked per request, so the surface can be turned off
///     without a restart like the map UI's.
/// </summary>
public sealed class ControlOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":Control";

    /// <summary>
    ///     Whether the control endpoints are served. On by default, consistent with
    ///     the map UI: this simulator has no security features at all by design (see
    ///     ARCHITECTURE.md), so gating a control surface behind a flag would be
    ///     security theatre rather than security. Turn it off when the situation must
    ///     only ever be driven through the TacticalAPI contract itself - a conformance
    ///     run, or a demo nobody should be able to reset from a browser tab.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
