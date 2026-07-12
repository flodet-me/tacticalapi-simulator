using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Periodically marks objects with an elapsed expiry_time as deleted, matching
///     the contract's "expired symbols are automatically marked as deleted".
///     Interval comes from IOptionsMonitor and is re-read every sweep.
/// </summary>
public sealed class ExpirySweeper(
    SituationStore store,
    IOptionsMonitor<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpirySweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options1 = options.CurrentValue;

            try
            {
                var expired = store.SweepExpired(timeProvider.GetUtcNow(), options1.ReporterId);
                if (expired > 0) logger.SweepCompleted(expired);
                else logger.SweepNoExpired();
            }
            catch (Exception ex)
            {
                // A sweep failure would otherwise be completely silent - there's no
                // caller to report it to, unlike SimulationSourceRunner's ingest path.
                logger.SweepFailed(ex);
            }

            await Task.Delay(options1.ExpirySweepInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
