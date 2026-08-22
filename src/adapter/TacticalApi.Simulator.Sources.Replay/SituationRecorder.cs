using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Sources.Replay.Logging;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>
///     Records a TacticalAPI endpoint's situation by subscribing to it exactly like
///     any other client would - <c>SubscribeSituationObjectEvents</c> against
///     whatever <c>Adapter:Ingest:Address</c> points at - and writing every event it
///     receives to a replayable recording.
///     Because it captures from the read side of the contract, it works against any
///     implementation and records everything happening there, not just traffic this
///     repo's own adapters produced. The cost is that it records the situation's
///     resulting state rather than the updates that caused it: what goes into the
///     recording is each object turned back into the update that would recreate it
///     (see <see cref="SituationObjectToUpdate" />), which is faithful for replay but
///     is a reconstruction, not the original bytes. When the traffic is your own
///     adapter's, "Adapter:Recording" records the real thing instead.
///     Deleted objects are recorded as deletes rather than updates, so a replay
///     reproduces the removal instead of resurrecting the object.
/// </summary>
public sealed class SituationRecorder(
    Situation.SituationClient client,
    IOptions<RecorderOptions> options,
    IOptionsMonitor<GrpcIngestOptions> ingestOptions,
    TimeProvider time,
    ILogger<SituationRecorder> logger)
    : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private long _frames;
    private long _objects;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        var reporter = new Identity { StringIdentity = RecorderOptions.Name };
        await using var writer = new RecordingWriter(settings.Path, time, logger);

        while (!stoppingToken.IsCancellationRequested)
            try
            {
                await RecordStreamAsync(writer, settings, reporter, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex)
            {
                // The endpoint went away or was never there; a recorder that gives up
                // on the first blip is useless for long captures.
                logger.RecorderStreamFailed(ex, ingestOptions.CurrentValue.Address, ReconnectDelay.TotalSeconds);
                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }

        logger.RecorderStopped(_frames, _objects);
    }

    private async Task RecordStreamAsync(
        RecordingWriter writer, RecorderOptions settings, Identity reporter, CancellationToken stoppingToken)
    {
        using var call = client.SubscribeSituationObjectEvents(
            new SubscribeSituationObjectEventsRequest(), cancellationToken: stoppingToken);

        logger.RecorderSubscribed(ingestOptions.CurrentValue.Address, settings.Path);

        // Per the contract the stream opens with the full current situation; whether
        // that belongs in the recording is the caller's call (see IncludeInitialSnapshot).
        var isFirstBatch = true;

        await foreach (var response in call.ResponseStream.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            var isSnapshot = isFirstBatch;
            isFirstBatch = false;

            if (isSnapshot && !settings.IncludeInitialSnapshot)
            {
                logger.RecorderSnapshotSkipped(response.SituationObjects.Count);
                continue;
            }

            var (updates, deletes) = Split(response.SituationObjects, reporter, time.GetUtcNow());
            if (updates.Count == 0 && deletes.Count == 0) continue;

            await writer.WriteAsync(updates, deletes, stoppingToken).ConfigureAwait(false);
            _frames = writer.FrameCount;
            _objects += updates.Count + deletes.Count;
        }
    }

    private static (List<UpdateSituationObject> Updates, List<DeleteSituationObject> Deletes) Split(
        IEnumerable<SituationObject> objects, Identity reporter, DateTimeOffset now)
    {
        var timestamp = Timestamp.FromDateTimeOffset(now);
        var updates = new List<UpdateSituationObject>();
        var deletes = new List<DeleteSituationObject>();

        foreach (var stored in objects)
            if (stored.IsDeleted?.Content == true)
            {
                if (SituationObjectToUpdate.IdentityOf(stored) is { } identity)
                    deletes.Add(new DeleteSituationObject
                    {
                        Identity = identity.Clone(),
                        Reporter = reporter,
                        ReportingTime = timestamp
                    });
            }
            else if (SituationObjectToUpdate.Convert(stored, reporter, timestamp) is { } update)
            {
                updates.Add(update);
            }

        return (updates, deletes);
    }
}
