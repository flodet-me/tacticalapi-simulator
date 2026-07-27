using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Non-generic constants shared by every closed <see cref="SimulationSourceRunner{TSource}" />.
///     A <c>static readonly</c> field declared directly on the generic type would instead get its
///     own separate storage per closed type (one per distinct <c>TSource</c>) - harmless for an
///     immutable constant like this one, but exactly the kind of surprise a shared static on a
///     generic type invites, so it lives here instead.
/// </summary>
internal static class SimulationSourceRunner
{
    internal static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(5);
}

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
    private readonly TSource _source = source;
    private long _cycle;

    // Starts true so a source that's disabled from the very first tick logs
    // that transition too, instead of only ever logging "enabled -> disabled".
    private bool _wasEnabled = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.RunnerStarted(_source.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_source.Enabled)
            {
                if (_wasEnabled)
                {
                    logger.SourceDisabled(_source.Name);
                    _wasEnabled = false;
                }

                await Task.Delay(SimulationSourceRunner.DisabledPollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (!_wasEnabled)
            {
                logger.SourceEnabled(_source.Name);
                _wasEnabled = true;
            }

            // Scope carries source/cycle context to every log entry emitted during
            // this cycle - including ones from the source's own ProduceAsync logger -
            // so both plain-text consoles and structured-logging backends can
            // correlate them without repeating the source name and cycle number in
            // every message. A message-template scope (rather than a bare
            // Dictionary) renders readably under the default console formatter too.
            _cycle++;
            using var scope = logger.BeginScope("SourceName={SourceName} Cycle={Cycle}", _source.Name, _cycle);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var updates = await _source.ProduceAsync(stoppingToken).ConfigureAwait(false);
                logger.CycleProduced(_source.Name, _cycle, updates.Count, stopwatch.Elapsed.TotalMilliseconds);

                if (updates.Count > 0)
                {
                    var result = await ingest.AddOrUpdateAsync(updates, stoppingToken).ConfigureAwait(false);
                    if (!result.Success)
                        logger.IngestFailed(_source.Name, result.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ProduceFailed(ex, _source.Name);
            }

            await Task.Delay(_source.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
