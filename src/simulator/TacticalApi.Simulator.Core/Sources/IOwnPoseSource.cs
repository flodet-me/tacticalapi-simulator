using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     A pluggable producer of this system's own position, pushed to
///     <c>UpdatePosition</c> by <see cref="OwnPoseSourceRunner{TSource}" />.
///     Register one with <c>services.AddOwnPoseSource&lt;MySource&gt;()</c>.
/// </summary>
public interface IOwnPoseSource : ISimulationSourceSchedule
{
    /// <summary>
    ///     Produces the current fix, or null to report nothing this cycle - which is
    ///     how a source simulates a sensor dropping out and lets the implementation's
    ///     own staleness handling take over. Exceptions are logged and the source is
    ///     retried next cycle.
    /// </summary>
    public Task<UpdatePosition?> ProducePositionAsync(CancellationToken cancellationToken);
}
