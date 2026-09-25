using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Drives a single <see cref="ISimulationSource" />: produce -> ingest -> wait
///     -> repeat, with the scheduling and failure handling inherited from
///     <see cref="SourceRunner{TSource}" />.
/// </summary>
public sealed class SimulationSourceRunner<TSource>(
    TSource source,
    ISituationIngest ingest,
    ILogger<SimulationSourceRunner<TSource>> logger)
    : SourceRunner<TSource>(source, logger)
    where TSource : ISimulationSource
{
    /// <inheritdoc/>
    protected override async Task RunCycleAsync(long cycle, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stopwatch);

        var updates = await Source.ProduceAsync(cancellationToken).ConfigureAwait(false);
        logger.CycleProduced(Source.Name, cycle, updates.Count, stopwatch.Elapsed.TotalMilliseconds);

        if (updates.Count == 0) return;

        var result = await ingest.AddOrUpdateAsync(updates, cancellationToken).ConfigureAwait(false);
        if (!result.Success) logger.IngestFailed(Source.Name, result.ErrorMessage);
    }
}
