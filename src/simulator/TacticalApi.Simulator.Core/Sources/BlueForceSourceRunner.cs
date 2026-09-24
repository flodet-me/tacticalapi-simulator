using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Drives a single <see cref="IBlueForceSource" />, pushing what it produces at
///     <c>AddOrUpdateBlueForces</c>. Scheduling and failure handling are inherited
///     from <see cref="SourceRunner{TSource}" />.
/// </summary>
public sealed class BlueForceSourceRunner<TSource>(
    TSource source,
    IBlueForceIngest ingest,
    ILogger<BlueForceSourceRunner<TSource>> logger)
    : SourceRunner<TSource>(source, logger)
    where TSource : IBlueForceSource
{
    /// <inheritdoc/>
    protected override async Task RunCycleAsync(long cycle, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopwatch);

        var updates = await Source.ProduceBlueForcesAsync(cancellationToken).ConfigureAwait(false);
        logger.CycleProduced(Source.Name, cycle, updates.Count, stopwatch.Elapsed.TotalMilliseconds);

        if (updates.Count == 0) return;

        var result = await ingest.AddOrUpdateAsync(updates, cancellationToken).ConfigureAwait(false);
        if (!result.Success) logger.IngestFailed(Source.Name, result.ErrorMessage);
    }
}
