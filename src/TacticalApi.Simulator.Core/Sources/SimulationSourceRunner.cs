using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
/// Generic hosted service that drives a single <see cref="ISimulationSource"/>:
/// produce -> ingest -> wait -> repeat. One runner per source keeps sources
/// isolated (a slow or failing source never stalls the others).
/// </summary>
public sealed class SimulationSourceRunner<TSource> : BackgroundService
    where TSource : ISimulationSource
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(5);

    private readonly TSource _source;
    private readonly ISituationIngest _ingest;
    private readonly ILogger<SimulationSourceRunner<TSource>> _logger;

    public SimulationSourceRunner(TSource source, ISituationIngest ingest, ILogger<SimulationSourceRunner<TSource>> logger)
    {
        _source = source;
        _ingest = ingest;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Simulation source '{Source}' runner started", _source.Name);

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
                    var result = _ingest.AddOrUpdate(updates);
                    if (!result.Success)
                    {
                        _logger.LogWarning("Source '{Source}' ingest failed: {Error}", _source.Name, result.ErrorMessage);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Source '{Source}' failed; retrying next cycle", _source.Name);
            }

            await Task.Delay(_source.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
