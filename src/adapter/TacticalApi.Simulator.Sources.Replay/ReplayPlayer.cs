using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Sources.Replay.Logging;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>
///     Pushes a recording back into a TacticalAPI endpoint, preserving the timing it
///     was captured with (optionally scaled by <see cref="ReplayOptions.Speed" />).
///     A <see cref="BackgroundService" /> rather than an <c>ISimulationSource</c>,
///     deliberately: a source is a produce-every-N-seconds loop that only ever emits
///     updates, and a replay is neither - it owns its own clock, and it has to be
///     able to reproduce deletes as deletes. It still writes through the same
///     <see cref="ISituationIngest" /> every source uses, so it drives any endpoint
///     <c>Adapter:Ingest:Address</c> points at, on the same terms.
/// </summary>
public sealed class ReplayPlayer(
    ISituationIngest ingest,
    IOptionsMonitor<ReplayOptions> options,
    TimeProvider time,
    ILogger<ReplayPlayer> logger)
    : BackgroundService
{
    private static readonly TimeSpan MissingFileRetryDelay = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enabled) return;

        var frames = await LoadAsync(stoppingToken).ConfigureAwait(false);
        if (frames is null || frames.Count == 0) return;

        var settings = options.CurrentValue;
        logger.ReplayStarted(frames.Count, settings.Path, settings.Speed, frames[^1].Offset.TotalSeconds);

        do
        {
            await PlayOnceAsync(frames, stoppingToken).ConfigureAwait(false);
            logger.ReplayFinished(settings.Path, frames.Count, options.CurrentValue.Loop);
        } while (options.CurrentValue.Loop && !stoppingToken.IsCancellationRequested);
    }

    private async Task<IReadOnlyList<RecordedFrame>?> LoadAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var path = options.CurrentValue.Path;
            if (File.Exists(path)) return await RecordingReader.ReadAllAsync(path, logger, stoppingToken)
                .ConfigureAwait(false);

            // The recording may simply not have been made yet - a record run and a
            // replay run are often the same command twice with the config flipped.
            logger.ReplayFileMissing(path);
            await Task.Delay(MissingFileRetryDelay, stoppingToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task PlayOnceAsync(IReadOnlyList<RecordedFrame> frames, CancellationToken stoppingToken)
    {
        var startedAt = time.GetUtcNow();
        var next = 0;

        while (next < frames.Count && !stoppingToken.IsCancellationRequested)
        {
            var settings = options.CurrentValue;

            // Re-read the speed every tick so it can be changed mid-replay like every
            // other option here; elapsed is measured in recording time, not wall time.
            var elapsed = (time.GetUtcNow() - startedAt) * settings.Speed;

            while (next < frames.Count && frames[next].Offset <= elapsed)
            {
                await SendAsync(frames[next], settings, stoppingToken).ConfigureAwait(false);
                next++;
            }

            if (next < frames.Count)
                await Task.Delay(settings.TickInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(RecordedFrame frame, ReplayOptions settings, CancellationToken stoppingToken)
    {
        var updates = frame.Updates;
        var deletes = frame.Deletes;

        if (settings.RestampReportingTime)
        {
            var now = Timestamp.FromDateTimeOffset(time.GetUtcNow());
            updates = Restamp(updates, now);
            deletes = Restamp(deletes, now);
        }

        if (updates.Count > 0)
        {
            var result = await ingest.AddOrUpdateAsync(updates, stoppingToken).ConfigureAwait(false);
            if (!result.Success) logger.ReplayIngestFailed(frame.Sequence, result.ErrorMessage);
        }

        if (deletes.Count > 0)
        {
            var result = await ingest.DeleteAsync(deletes, stoppingToken).ConfigureAwait(false);
            if (!result.Success) logger.ReplayIngestFailed(frame.Sequence, result.ErrorMessage);
        }

        logger.ReplayFrameSent(frame.Sequence, updates.Count, deletes.Count);
    }

    private static List<UpdateSituationObject> Restamp(IReadOnlyList<UpdateSituationObject> updates, Timestamp now)
    {
        var result = new List<UpdateSituationObject>(updates.Count);
        foreach (var update in updates)
        {
            var copy = update.Clone();
            ReplayTimestamps.Restamp(copy, now);
            result.Add(copy);
        }

        return result;
    }

    private static List<DeleteSituationObject> Restamp(IReadOnlyList<DeleteSituationObject> deletes, Timestamp now)
    {
        var result = new List<DeleteSituationObject>(deletes.Count);
        foreach (var delete in deletes)
        {
            var copy = delete.Clone();
            copy.ReportingTime = now;
            result.Add(copy);
        }

        return result;
    }
}
