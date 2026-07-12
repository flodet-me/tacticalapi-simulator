using Grpc.Core;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Logging;

namespace TacticalApi.Simulator.Host.Services;

/// <summary>
///     gRPC implementation of rheinmetall.tactical_api.v0.Situation. Thin layer:
///     all state handling lives in the core; this class only translates between
///     RPC messages and the store/broker.
/// </summary>
public sealed class SituationGrpcService : Situation.SituationBase
{
    private readonly SituationEventBroker _broker;
    private readonly ISituationIngest _ingest;
    private readonly ILogger<SituationGrpcService> _logger;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly SituationStore _store;

    /// <summary>Creates the service with its store/broker/ingest/config/logging dependencies.</summary>
    public SituationGrpcService(
        SituationStore store,
        SituationEventBroker broker,
        ISituationIngest ingest,
        IOptionsMonitor<SimulatorOptions> options,
        ILogger<SituationGrpcService> logger)
    {
        _store = store;
        _broker = broker;
        _ingest = ingest;
        _options = options;
        _logger = logger;
    }

    /// <summary>Returns the full current snapshot of non-deleted situation objects.</summary>
    public override Task<GetSituationObjectsResponse> GetSituationObjects(
        GetSituationObjectsRequest request, ServerCallContext context)
    {
        var response = new GetSituationObjectsResponse
        {
            Header = new ResponseHeader { Success = true }
        };
        response.SituationObjects.AddRange(_store.GetSnapshot());
        _logger.GetSituationObjectsServed(response.SituationObjects.Count);
        return Task.FromResult(response);
    }

    /// <summary>Creates or updates the given situation objects in the store.</summary>
    public override Task<AddOrUpdateSituationObjectsResponse> AddOrUpdateSituationObjects(
        AddOrUpdateSituationObjectsRequest request, ServerCallContext context)
    {
        _logger.AddOrUpdateReceived(request.SituationObjects.Count);
        var result = _ingest.AddOrUpdate(request.SituationObjects);
        if (!result.Success) _logger.AddOrUpdateFailed(result.ErrorMessage);
        return Task.FromResult(new AddOrUpdateSituationObjectsResponse { Header = result.ToHeader() });
    }

    /// <summary>Marks the given situation objects as deleted.</summary>
    public override Task<DeleteSituationObjectsResponse> DeleteSituationObjects(
        DeleteSituationObjectsRequest request, ServerCallContext context)
    {
        _logger.DeleteReceived(request.SituationObjects.Count);
        var result = _ingest.Delete(request.SituationObjects);
        if (!result.Success) _logger.DeleteFailed(result.ErrorMessage);
        return Task.FromResult(new DeleteSituationObjectsResponse { Header = result.ToHeader() });
    }

    /// <summary>Streams an initial snapshot followed by live change events until the client disconnects.</summary>
    public override async Task SubscribeSituationObjectEvents(
        SubscribeSituationObjectEventsRequest request,
        IServerStreamWriter<SubscribeSituationObjectEventsResponse> responseStream,
        ServerCallContext context)
    {
        var batchSize = _options.CurrentValue.Performance.StreamBatchSize;

        // Scope tags every log line for this subscription's lifetime with its peer,
        // so connect/batch/disconnect entries can be correlated without repeating
        // {Peer} in every message. A message-template scope renders readably under
        // the default console formatter, unlike a bare Dictionary state object.
        using var scope = _logger.BeginScope("Peer={Peer}", context.Peer);
        _logger.SubscriberConnected(context.Peer);

        // Subscribe BEFORE taking the snapshot so no change is lost in between.
        // An object updated during the snapshot may be delivered twice, which
        // is harmless because updates carry full object state.
        using var subscription = _broker.Subscribe();

        // Initial snapshot: "all non-deleted existing situation objects" per contract.
        var snapshot = _store.GetSnapshot();
        for (var offset = 0; offset < snapshot.Count; offset += batchSize)
        {
            var response = CreateResponse();
            for (var i = offset; i < Math.Min(offset + batchSize, snapshot.Count); i++)
                response.SituationObjects.Add(snapshot[i]);

            await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
            _logger.SnapshotBatchSent(context.Peer, response.SituationObjects.Count);
        }

        // Live events: drain everything available into one batched response to
        // minimize per-message overhead under load.
        try
        {
            var reader = subscription.Reader;
            while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                var response = CreateResponse();
                while (response.SituationObjects.Count < batchSize && reader.TryRead(out var obj))
                    response.SituationObjects.Add(obj);

                await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
                _logger.EventBatchSent(context.Peer, response.SituationObjects.Count);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client went away - normal for long-lived streams.
        }
        finally
        {
            _logger.SubscriberDisconnected(context.Peer);
        }
    }

    private static SubscribeSituationObjectEventsResponse CreateResponse()
    {
        return new SubscribeSituationObjectEventsResponse
        {
            Header = new ResponseHeader { Success = true }
        };
    }
}
