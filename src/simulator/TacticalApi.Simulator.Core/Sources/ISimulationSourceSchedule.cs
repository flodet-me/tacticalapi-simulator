namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     What every kind of simulation source has in common: a name to log under,
///     whether it should be running, and how often it is asked to produce.
///     Split out from <see cref="ISimulationSource" /> once the contract grew a
///     second and third service to feed: a blue force source and an own-position
///     source are scheduled identically to a situation source and differ only in
///     what they hand back, so the scheduling half lives here and
///     <see cref="SourceRunner{TSource}" /> drives all three.
/// </summary>
public interface ISimulationSourceSchedule
{
    /// <summary>Stable name used for logging and diagnostics.</summary>
    public string Name { get; }

    /// <summary>Whether the source should currently run (can react to live config).</summary>
    public bool Enabled { get; }

    /// <summary>Delay between production cycles; re-read every cycle.</summary>
    public TimeSpan Interval { get; }
}
