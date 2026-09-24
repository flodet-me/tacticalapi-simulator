using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     <see cref="IBlueForceIngest" /> implemented as a real gRPC client of
///     <c>rheinmetall.tactical_api.v0.BlueForceTracking</c>, calling
///     <c>AddOrUpdateBlueForces</c> against whatever endpoint
///     <see cref="GrpcIngestOptions" /> points at.
/// </summary>
public sealed class GrpcBlueForceIngest(BlueForceTracking.BlueForceTrackingClient client) : IBlueForceIngest
{
    /// <inheritdoc/>
    public async Task<IngestResult> AddOrUpdateAsync(
        IReadOnlyList<UpdateBlueForce> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0) return IngestResult.Ok;

        var request = new AddOrUpdateBlueForcesRequest();
        request.BlueForcesToUpdates.AddRange(updates);

        try
        {
            var response = await client
                .AddOrUpdateBlueForcesAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Header.Success ? IngestResult.Ok : IngestResult.Fail(response.Header.ErrorMessage);
        }
        catch (RpcException ex)
        {
            return IngestResult.Fail($"gRPC call failed: {ex.Status.Detail}");
        }
    }
}
