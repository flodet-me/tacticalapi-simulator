using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     <see cref="ISituationIngest" /> implemented as a real gRPC client of
///     <c>rheinmetall.tactical_api.v0.Situation</c>, calling
///     <c>AddOrUpdateSituationObjects</c>/<c>DeleteSituationObjects</c> against
///     whatever endpoint <see cref="GrpcIngestOptions" /> points at.
/// </summary>
public sealed class GrpcSituationIngest(Situation.SituationClient client) : ISituationIngest
{
    /// <inheritdoc/>
    public async Task<IngestResult> AddOrUpdateAsync(
        IReadOnlyList<UpdateSituationObject> updates, CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return IngestResult.Ok;

        var request = new AddOrUpdateSituationObjectsRequest();
        request.SituationObjects.AddRange(updates);

        try
        {
            var response = await client
                .AddOrUpdateSituationObjectsAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Header.Success ? IngestResult.Ok : IngestResult.Fail(response.Header.ErrorMessage);
        }
        catch (RpcException ex)
        {
            return IngestResult.Fail($"gRPC call failed: {ex.Status.Detail}");
        }
    }

    /// <inheritdoc/>
    public async Task<IngestResult> DeleteAsync(
        IReadOnlyList<DeleteSituationObject> deletes, CancellationToken cancellationToken = default)
    {
        if (deletes.Count == 0) return IngestResult.Ok;

        var request = new DeleteSituationObjectsRequest();
        request.SituationObjects.AddRange(deletes);

        try
        {
            var response = await client
                .DeleteSituationObjectsAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Header.Success ? IngestResult.Ok : IngestResult.Fail(response.Header.ErrorMessage);
        }
        catch (RpcException ex)
        {
            return IngestResult.Fail($"gRPC call failed: {ex.Status.Detail}");
        }
    }
}
