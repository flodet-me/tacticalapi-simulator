using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Periodically re-checks whether the primary position has gone stale and
///     announces it when it has.
///     Without this the flip to <c>is_invalid_or_expired</c> would only ever be
///     observed by someone who called <c>GetPosition</c>, and a subscriber - who by
///     definition is not calling anything - would sit on a fix that quietly stopped
///     being true. That subscriber is the one client most worth simulating for.
/// </summary>
public sealed class OwnPoseStalenessSweeper(
    OwnPoseStore store,
    IOptionsMonitor<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<OwnPoseStalenessSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue.OwnPose;

            try
            {
                store.RefreshValidity(timeProvider.GetUtcNow());
            }
            catch (Exception ex)
            {
                logger.OwnPoseSweepFailed(ex);
            }

            await Task.Delay(settings.SweepInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
