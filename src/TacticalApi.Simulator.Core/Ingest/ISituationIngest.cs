using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     The interface simulation sources use to submit writes. Implemented as a
///     genuine gRPC client against a configurable TacticalAPI endpoint (see
///     <see cref="GrpcIngestOptions" />) - any implementation of the contract,
///     not necessarily this repo's own <c>Host</c>, which is just the default.
///     A source is therefore a real external client of the interface, not a
///     special case that happens to be talking to "the simulator".
/// </summary>
public interface ISituationIngest
{
    /// <summary>Applies add/update messages. Returns per-batch success.</summary>
    public Task<IngestResult> AddOrUpdateAsync(
        IReadOnlyList<UpdateSituationObject> updates, CancellationToken cancellationToken = default);

    /// <summary>Marks objects as deleted.</summary>
    public Task<IngestResult> DeleteAsync(
        IReadOnlyList<DeleteSituationObject> deletes, CancellationToken cancellationToken = default);
}
