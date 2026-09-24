using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     The interface blue force sources use to submit keep-alives. Implemented as a
///     genuine gRPC client of <c>BlueForceTracking</c> against a configurable
///     endpoint (see <see cref="GrpcIngestOptions" />) - any implementation of the
///     contract, not necessarily this repo's own <c>Host</c>.
///     Separate from <see cref="ISituationIngest" /> because it is a separate
///     service in the contract, with its own write semantics: every call carries
///     the blue force whole, and calling it is itself the keep-alive that stops the
///     blue force being deleted.
/// </summary>
public interface IBlueForceIngest
{
    /// <summary>Adds or updates blue forces. Returns per-batch success.</summary>
    public Task<IngestResult> AddOrUpdateAsync(
        IReadOnlyList<UpdateBlueForce> updates, CancellationToken cancellationToken = default);
}
