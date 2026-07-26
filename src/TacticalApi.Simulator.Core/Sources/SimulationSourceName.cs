namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Derives an <see cref="ISimulationSource.Name" /> from its options'
///     configuration section path, so the diagnostic name and the section it's
///     bound from can't drift apart (e.g. "Simulator:OpenSky" -&gt; "OpenSky").
/// </summary>
public static class SimulationSourceName
{
    /// <summary>Returns the last colon-separated segment of <paramref name="sectionName" />.</summary>
    public static string FromSectionName(string sectionName)
    {
        return sectionName[(sectionName.LastIndexOf(':') + 1)..];
    }
}
