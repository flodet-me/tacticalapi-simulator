using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     A pluggable producer of simulated blue forces, pushed to
///     <c>AddOrUpdateBlueForces</c> by <see cref="BlueForceSourceRunner{TSource}" />.
///     Register one with <c>services.AddBlueForceSource&lt;MySource&gt;()</c>.
///     Note what <see cref="ISimulationSourceSchedule.Interval" /> means here: the
///     call is itself the keep-alive, so an interval above the implementation's
///     timeout will make this source's blue forces flicker in and out. The contract
///     asks for at least every 30s; treat that as the ceiling, not the target.
/// </summary>
public interface IBlueForceSource : ISimulationSourceSchedule
{
    /// <summary>
    ///     Produces the next batch of blue force updates. Returning an empty batch
    ///     is fine - it just means nothing is keeping those blue forces alive this
    ///     cycle. Exceptions are logged and the source is retried next cycle.
    /// </summary>
    public Task<IReadOnlyList<UpdateBlueForce>> ProduceBlueForcesAsync(CancellationToken cancellationToken);
}
