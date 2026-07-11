using Grpc.Core;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Store;

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

    public override Task<GetSituationObjectsResponse> GetSituationObjects(
        GetSituationObjectsRequest request, ServerCallContext context)
    {
        var response = new GetSituationObjectsResponse
        {
            Header = new ResponseHeader { Success = true }
        };
        response.SituationObjects.AddRange(_store.GetSnapshot());
        return Task.FromResult(response);
    }

    public override Task<AddOrUpdateSituationObjectsResponse> AddOrUpdateSituationObjects(
        AddOrUpdateSituationObjectsRequest request, ServerCallContext context)
    {
        var result = _ingest.AddOrUpdate(request.SituationObjects);
        return Task.FromResult(new AddOrUpdateSituationObjectsResponse { Header = result.ToHeader() });
    }

    public override Task<DeleteSituationObjectsResponse> DeleteSituationObjects(
        DeleteSituationObjectsRequest request, ServerCallContext context)
    {
        var result = _ingest.Delete(request.SituationObjects);
        return Task.FromResult(new DeleteSituationObjectsResponse { Header = result.ToHeader() });
    }

    public override async Task SubscribeSituationObjectEvents(
        SubscribeSituationObjectEventsRequest request,
        IServerStreamWriter<SubscribeSituationObjectEventsResponse> responseStream,
        ServerCallContext context)
    {
        var batchSize = _options.CurrentValue.Performance.StreamBatchSize;
        _logger.LogInformation("Subscriber {Peer} connected", context.Peer);

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
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client went away - normal for long-lived streams.
        }
        finally
        {
            _logger.LogInformation("Subscriber {Peer} disconnected", context.Peer);
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
