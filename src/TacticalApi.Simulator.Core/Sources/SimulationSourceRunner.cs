using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Generic hosted service that drives a single <see cref="ISimulationSource" />:
///     produce -> ingest -> wait -> repeat. One runner per source keeps sources
///     isolated (a slow or failing source never stalls the others).
/// </summary>
public sealed class SimulationSourceRunner<TSource>(
    TSource source,
    ISituationIngest ingest,
    ILogger<SimulationSourceRunner<TSource>> logger)
    : BackgroundService
    where TSource : ISimulationSource
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(5);

    private readonly TSource _source = source;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Simulation source '{Source}' runner started", _source.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_source.Enabled)
            {
                await Task.Delay(DisabledPollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var updates = await _source.ProduceAsync(stoppingToken).ConfigureAwait(false);
                if (updates.Count > 0)
                {
                    var result = ingest.AddOrUpdate(updates);
                    if (!result.Success)
                        logger.LogWarning("Source '{Source}' ingest failed: {Error}", _source.Name,
                            result.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Source '{Source}' failed; retrying next cycle", _source.Name);
            }

            await Task.Delay(_source.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
