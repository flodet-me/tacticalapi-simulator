using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
/// A pluggable producer of simulated situation data. Implementations emit the
/// plain TacticalAPI update model (<see cref="UpdateSituationObject"/>) - no
/// internal abstraction in between - and the runner feeds those into the same
/// ingest path the gRPC interface uses.
///
/// To add a new source (e.g. an AIS ship tracker):
///  1. Implement this interface.
///  2. Register it: services.AddSimulationSource&lt;MySource&gt;().
///  3. Bind its options from configuration (IOptionsMonitor recommended so
///     interval/parameters are tunable at runtime).
/// </summary>
public interface ISimulationSource
{
    /// <summary>Stable name used for logging and diagnostics.</summary>
    public string Name { get; }

    /// <summary>Whether the source should currently run (can react to live config).</summary>
    public bool Enabled { get; }

    /// <summary>Delay between production cycles; re-read every cycle.</summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Produces the next batch of updates. Returning an empty batch is fine.
    /// Exceptions are logged and the source is retried next cycle.
    /// </summary>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken);
}
