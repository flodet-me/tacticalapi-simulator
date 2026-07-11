using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
/// Periodically marks objects with an elapsed expiry_time as deleted, matching
/// the contract's "expired symbols are automatically marked as deleted".
/// Interval comes from IOptionsMonitor and is re-read every sweep.
/// </summary>
public sealed class ExpirySweeper : BackgroundService
{
    private readonly SituationStore _store;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExpirySweeper> _logger;

    public ExpirySweeper(
        SituationStore store,
        IOptionsMonitor<SimulatorOptions> options,
        TimeProvider timeProvider,
        ILogger<ExpirySweeper> logger)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            var expired = _store.SweepExpired(_timeProvider.GetUtcNow(), options.ReporterId);
            if (expired > 0)
            {
                _logger.LogInformation("Marked {Count} expired situation objects as deleted", expired);
            }

            await Task.Delay(options.ExpirySweepInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
