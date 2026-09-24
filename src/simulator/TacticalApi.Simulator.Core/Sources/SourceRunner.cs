using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Sources;

/// <summary>
///     Non-generic constants shared by every closed <see cref="SourceRunner{TSource}" />.
///     A <c>static readonly</c> field declared directly on the generic type would instead get its
///     own separate storage per closed type (one per distinct <c>TSource</c>) - harmless for an
///     immutable constant like this one, but exactly the kind of surprise a shared static on a
///     generic type invites, so it lives here instead.
/// </summary>
internal static class SourceRunner
{
    internal static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
///     The scheduling half of every source runner: enable/disable transitions, the
///     cycle counter and its log scope, the retry-on-exception policy, and the wait
///     between cycles. One runner per source keeps sources isolated (a slow or
///     failing source never stalls the others).
///     What a cycle actually does - produce situation objects, blue forces, or a
///     position, and push them at the matching RPC - is the derived runner's job.
/// </summary>
/// <typeparam name="TSource">The source this runner drives.</typeparam>
public abstract class SourceRunner<TSource> : BackgroundService
    where TSource : ISimulationSourceSchedule
{
    private readonly ILogger _logger;
    private readonly TSource _source;
    private long _cycle;

    // Starts true so a source that's disabled from the very first tick logs
    // that transition too, instead of only ever logging "enabled -> disabled".
    private bool _wasEnabled = true;

    /// <summary>Creates a runner for <paramref name="source" />.</summary>
    protected SourceRunner(TSource source, ILogger logger)
    {
        _source = source;
        _logger = logger;
    }

    /// <summary>The source being driven, for the derived runner's own cycle.</summary>
    protected TSource Source => _source;

    /// <summary>
    ///     Runs one production cycle and pushes whatever it produced. Called only
    ///     while the source is enabled, inside the cycle's log scope and the runner's
    ///     exception handling.
    /// </summary>
    /// <param name="cycle">1-based cycle number, for logging.</param>
    /// <param name="stopwatch">Started at the top of this cycle, for logging.</param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    protected abstract Task RunCycleAsync(long cycle, Stopwatch stopwatch, CancellationToken cancellationToken);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.RunnerStarted(_source.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_source.Enabled)
            {
                if (_wasEnabled)
                {
                    _logger.SourceDisabled(_source.Name);
                    _wasEnabled = false;
                }

                await Task.Delay(SourceRunner.DisabledPollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (!_wasEnabled)
            {
                _logger.SourceEnabled(_source.Name);
                _wasEnabled = true;
            }

            // Scope carries source/cycle context to every log entry emitted during
            // this cycle - including ones from the source's own producer logger -
            // so both plain-text consoles and structured-logging backends can
            // correlate them without repeating the source name and cycle number in
            // every message. A message-template scope (rather than a bare
            // Dictionary) renders readably under the default console formatter too.
            _cycle++;
            using var scope = _logger.BeginScope("SourceName={SourceName} Cycle={Cycle}", _source.Name, _cycle);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await RunCycleAsync(_cycle, stopwatch, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ProduceFailed(ex, _source.Name);
            }

            await Task.Delay(_source.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
