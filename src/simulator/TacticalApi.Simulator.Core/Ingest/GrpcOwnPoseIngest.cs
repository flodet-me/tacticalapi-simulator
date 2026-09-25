using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     <see cref="IOwnPoseIngest" /> implemented as a real gRPC client of
///     <c>rheinmetall.tactical_api.v0.OwnPose</c>, calling <c>UpdatePosition</c>
///     against whatever endpoint <see cref="GrpcIngestOptions" /> points at.
/// </summary>
public sealed class GrpcOwnPoseIngest(OwnPose.OwnPoseClient client) : IOwnPoseIngest
{
    /// <inheritdoc/>
    public async Task<IngestResult> UpdatePositionAsync(
        UpdatePosition position, CancellationToken cancellationToken = default)
    {
        var request = new UpdatePositionRequest { Position = position };

        try
        {
            var response = await client
                .UpdatePositionAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Header.Success ? IngestResult.Ok : IngestResult.Fail(response.Header.ErrorMessage);
        }
        catch (RpcException ex)
        {
            return IngestResult.Fail($"gRPC call failed: {ex.Status.Detail}");
        }
    }
}
