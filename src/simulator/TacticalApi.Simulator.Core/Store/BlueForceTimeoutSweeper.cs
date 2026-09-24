using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Periodically deletes blue forces that stopped sending keep-alives, which is
///     the only deletion the <c>BlueForceTracking</c> contract has: "Deletion is
///     done implicitly when a timeout defined by the application is reached."
///     Interval and timeout come from IOptionsMonitor and are re-read every sweep.
/// </summary>
public sealed class BlueForceTimeoutSweeper(
    BlueForceStore store,
    IOptionsMonitor<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<BlueForceTimeoutSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue.BlueForce;

            try
            {
                store.SweepTimedOut(timeProvider.GetUtcNow());
            }
            catch (Exception ex)
            {
                // A sweep failure would otherwise be completely silent - there's no
                // caller to report it to, unlike an adapter's ingest path.
                logger.BlueForceSweepFailed(ex);
            }

            await Task.Delay(settings.SweepInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
