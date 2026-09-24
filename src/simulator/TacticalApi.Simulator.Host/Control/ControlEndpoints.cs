using Google.Protobuf;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Control;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Faults;
using TacticalApi.Simulator.Host.Logging;

namespace TacticalApi.Simulator.Host.Control;

/// <summary>
///     A small HTTP surface for driving the simulator itself: freeze it, reset it,
///     put an object into it by hand.
///     Deliberately NOT part of the <c>Situation</c> service. Nothing here exists in
///     the TacticalAPI contract, and adding non-contract RPCs to a simulator of that
///     contract would make it a worse simulator - a client could then depend on
///     something no real implementation offers. Keeping it on a separate HTTP path
///     means the gRPC surface stays exactly the contract and nothing else.
///     Injection still goes through the same <see cref="SituationStore" /> as every
///     gRPC write, so a hand-injected object is merged, validated, expired and
///     announced identically to one that arrived over the wire - it is a shortcut
///     past the transport, not past the semantics.
/// </summary>
public static class ControlEndpoints
{
    private const string BasePath = "/api/control";

    /// <summary>Maps every control endpoint onto <paramref name="app" />.</summary>
    public static WebApplication MapControlEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet($"{BasePath}/state", (
            SituationStore store,
            SituationEventBroker broker,
            BlueForceStore blueForces,
            BlueForceEventBroker blueForceBroker,
            OwnPoseStore ownPose,
            PositionEventBroker positionBroker,
            TimeProvider timeProvider,
            SimulationPause pause,
            IOptionsMonitor<FaultInjectionOptions> faults,
            IOptionsMonitor<ControlOptions> options) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            var fault = faults.CurrentValue;
            var position = ownPose.GetPosition(timeProvider.GetUtcNow());
            return Results.Ok(new
            {
                paused = pause.IsPaused,
                situationObjects = store.Count,
                subscribers = broker.SubscriberCount,
                blueForces = blueForces.Count,
                blueForceSubscribers = blueForceBroker.SubscriberCount,
                positionSources = ownPose.SourceCount,
                positionSubscribers = positionBroker.SubscriberCount,
                primaryPosition = position is null
                    ? null
                    : new
                    {
                        source = position.SourceIdentifier,
                        lat = position.PointLocation?.GeoPoint?.LatitudeCoordinate,
                        lon = position.PointLocation?.GeoPoint?.LongitudeCoordinate,
                        invalidOrExpired = position.IsInvalidOrExpired
                    },
                faults = new
                {
                    enabled = fault.Enabled,
                    fault.Seed,
                    latencyMs = fault.Latency.TotalMilliseconds,
                    latencyJitterMs = fault.LatencyJitter.TotalMilliseconds,
                    fault.RpcErrorProbability,
                    rpcStatusCode = fault.RpcStatusCode.ToString(),
                    fault.ErrorHeaderProbability,
                    fault.DropWriteProbability,
                    streamAbortAfterMs = fault.StreamAbortAfter?.TotalMilliseconds
                }
            });
        });

        app.MapPost($"{BasePath}/pause", (
            SimulationPause pause,
            IOptionsMonitor<ControlOptions> options,
            ILogger<SimulationPause> logger) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            var changed = pause.Pause();
            if (changed) logger.SituationPaused();
            return Results.Ok(new { paused = true, changed });
        });

        app.MapPost($"{BasePath}/resume", (
            SimulationPause pause,
            IOptionsMonitor<ControlOptions> options,
            ILogger<SimulationPause> logger) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            var changed = pause.Resume();
            if (changed) logger.SituationResumed();
            return Results.Ok(new { paused = false, changed });
        });

        app.MapPost($"{BasePath}/reset", (
            SituationStore store,
            BlueForceStore blueForces,
            OwnPoseStore ownPose,
            IOptionsMonitor<ControlOptions> options,
            ILogger<SituationStore> logger) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            // A reset returns the whole simulator to its just-started state, which
            // means all three services: a situation emptied while blue forces kept
            // reporting from the previous run would be a state no restart produces.
            var dropped = store.Clear();
            var droppedBlueForces = blueForces.Clear();
            var droppedPositionSources = ownPose.Clear();
            logger.SituationReset(dropped);
            logger.BlueForcesReset(droppedBlueForces, droppedPositionSources);
            return Results.Ok(new { dropped, droppedBlueForces, droppedPositionSources });
        });

        app.MapPost($"{BasePath}/objects", async (
            HttpRequest request,
            SituationStore store,
            IOptionsMonitor<ControlOptions> options,
            ILogger<SituationStore> logger) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            return await ApplyAsync<AddOrUpdateSituationObjectsRequest>(request, parsed =>
            {
                var result = store.AddOrUpdate(parsed.SituationObjects);
                if (result.Success) logger.ObjectsInjected(parsed.SituationObjects.Count);
                return (result.Success, result.ErrorMessage, parsed.SituationObjects.Count);
            }).ConfigureAwait(false);
        });

        app.MapPost($"{BasePath}/blueforces", async (
            HttpRequest request,
            BlueForceStore store,
            IOptionsMonitor<ControlOptions> options) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            return await ApplyAsync<AddOrUpdateBlueForcesRequest>(request, parsed =>
            {
                var result = store.AddOrUpdate(parsed.BlueForcesToUpdates);
                return (result.Success, result.ErrorMessage, parsed.BlueForcesToUpdates.Count);
            }).ConfigureAwait(false);
        });

        app.MapPost($"{BasePath}/position", async (
            HttpRequest request,
            OwnPoseStore store,
            TimeProvider timeProvider,
            IOptionsMonitor<ControlOptions> options) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            return await ApplyAsync<UpdatePositionRequest>(request, parsed =>
            {
                var result = store.UpdatePosition(parsed.Position, timeProvider.GetUtcNow());
                return (result.Success, result.ErrorMessage, parsed.Position is null ? 0 : 1);
            }).ConfigureAwait(false);
        });

        app.MapPost($"{BasePath}/objects/delete", async (
            HttpRequest request,
            SituationStore store,
            IOptionsMonitor<ControlOptions> options) =>
        {
            if (!options.CurrentValue.Enabled) return Results.NotFound();

            return await ApplyAsync<DeleteSituationObjectsRequest>(request, parsed =>
            {
                var result = store.Delete(parsed.SituationObjects);
                return (result.Success, result.ErrorMessage, parsed.SituationObjects.Count);
            }).ConfigureAwait(false);
        });

        return app;
    }

    /// <summary>
    ///     Parses a request body as the canonical protobuf JSON of
    ///     <typeparamref name="TRequest" /> and applies it.
    ///     Protobuf JSON rather than a bespoke DTO so the body is exactly the request
    ///     message of the contract - the same JSON <c>grpcurl -d</c> takes - which
    ///     keeps this endpoint from quietly becoming a second, differently-shaped way
    ///     to describe a situation object.
    /// </summary>
    private static async Task<IResult> ApplyAsync<TRequest>(
        HttpRequest request, Func<TRequest, (bool Success, string? Error, int Count)> apply)
        where TRequest : IMessage, new()
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        TRequest parsed;
        try
        {
            parsed = JsonParser.Default.Parse<TRequest>(body);
        }
        catch (InvalidJsonException ex)
        {
            return Results.BadRequest(new { error = $"Body is not valid JSON: {ex.Message}" });
        }
        catch (InvalidProtocolBufferException ex)
        {
            return Results.BadRequest(new
            {
                error = $"Body is not a valid {typeof(TRequest).Name}: {ex.Message}"
            });
        }

        var (success, error, count) = apply(parsed);
        return success
            ? Results.Ok(new { applied = count })
            : Results.BadRequest(new { error });
    }
}
