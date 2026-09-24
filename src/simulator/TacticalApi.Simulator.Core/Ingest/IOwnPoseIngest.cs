using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     The interface own-position sources use to report a fix. Implemented as a
///     genuine gRPC client of <c>OwnPose</c> against a configurable endpoint (see
///     <see cref="GrpcIngestOptions" />).
/// </summary>
public interface IOwnPoseIngest
{
    /// <summary>Reports one position source's current fix.</summary>
    public Task<IngestResult> UpdatePositionAsync(
        UpdatePosition position, CancellationToken cancellationToken = default);
}
