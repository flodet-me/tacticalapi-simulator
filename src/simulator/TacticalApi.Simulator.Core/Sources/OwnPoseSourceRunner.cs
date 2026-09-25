using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Drives a single <see cref="IOwnPoseSource" />, pushing what it produces at
///     <c>UpdatePosition</c>. Scheduling and failure handling are inherited from
///     <see cref="SourceRunner{TSource}" />.
/// </summary>
public sealed class OwnPoseSourceRunner<TSource>(
    TSource source,
    IOwnPoseIngest ingest,
    ILogger<OwnPoseSourceRunner<TSource>> logger)
    : SourceRunner<TSource>(source, logger)
    where TSource : IOwnPoseSource
{
    /// <inheritdoc/>
    protected override async Task RunCycleAsync(long cycle, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopwatch);

        var position = await Source.ProducePositionAsync(cancellationToken).ConfigureAwait(false);
        logger.CycleProduced(Source.Name, cycle, position is null ? 0 : 1, stopwatch.Elapsed.TotalMilliseconds);

        if (position is null) return;

        var result = await ingest.UpdatePositionAsync(position, cancellationToken).ConfigureAwait(false);
        if (!result.Success) logger.IngestFailed(Source.Name, result.ErrorMessage);
    }
}
