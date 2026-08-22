using Grpc.Core;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Faults;
using TacticalApi.Simulator.Host.Logging;

namespace TacticalApi.Simulator.Host.Services;

/// <summary>
///     gRPC implementation of rheinmetall.tactical_api.v0.Situation. Thin layer:
///     all state handling lives in the core; this class only translates between
///     RPC messages and the store/broker.
///     The one thing it does beyond translating is apply the faults that only make
///     sense in terms of these RPCs - an unsuccessful response header, a write
///     acknowledged but not applied, a subscription cut short. Transport-level
///     faults (latency, RPC errors) never reach here; see
///     <see cref="FaultInjectionInterceptor" />.
/// </summary>
public sealed class SituationGrpcService : Situation.SituationBase
{
    private readonly SituationEventBroker _broker;
    private readonly FaultInjector _faults;
    private readonly ILogger<SituationGrpcService> _logger;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly SituationStore _store;

    /// <summary>Creates the service with its store/broker/config/faults/logging dependencies.</summary>
    public SituationGrpcService(
        SituationStore store,
        SituationEventBroker broker,
        IOptionsMonitor<SimulatorOptions> options,
        FaultInjector faults,
        ILogger<SituationGrpcService> logger)
    {
        _store = store;
        _broker = broker;
        _options = options;
        _faults = faults;
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

        if (_faults.ShouldReturnErrorHeader(nameof(AddOrUpdateSituationObjects)))
            return Task.FromResult(new AddOrUpdateSituationObjectsResponse
            {
                Header = InjectedFailure()
            });

        // Acknowledged, never applied: the failure mode a client can only catch by
        // reconciling the situation afterwards.
        if (_faults.ShouldDropWrite(nameof(AddOrUpdateSituationObjects)))
            return Task.FromResult(new AddOrUpdateSituationObjectsResponse
            {
                Header = new ResponseHeader { Success = true }
            });

        var result = _store.AddOrUpdate(request.SituationObjects);
        if (!result.Success) _logger.AddOrUpdateFailed(result.ErrorMessage);
        return Task.FromResult(new AddOrUpdateSituationObjectsResponse { Header = result.ToHeader() });
    }

    /// <summary>Marks the given situation objects as deleted.</summary>
    public override Task<DeleteSituationObjectsResponse> DeleteSituationObjects(
        DeleteSituationObjectsRequest request, ServerCallContext context)
    {
        _logger.DeleteReceived(request.SituationObjects.Count);

        if (_faults.ShouldReturnErrorHeader(nameof(DeleteSituationObjects)))
            return Task.FromResult(new DeleteSituationObjectsResponse
            {
                Header = InjectedFailure()
            });

        if (_faults.ShouldDropWrite(nameof(DeleteSituationObjects)))
            return Task.FromResult(new DeleteSituationObjectsResponse
            {
                Header = new ResponseHeader { Success = true }
            });

        var result = _store.Delete(request.SituationObjects);
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

        // Fixed at subscription time, not re-read per batch: a stream's lifetime is a
        // property of that stream, and re-reading it would move the deadline around
        // underneath a subscriber whenever the config file was touched.
        // Driven by a timer on a linked token rather than a deadline checked per
        // batch, so an idle stream - the case reconnect logic is least likely to have
        // been tested against - is cut off on time instead of surviving indefinitely
        // just because nothing happened to be flowing.
        var lifetime = _faults.StreamLifetime();
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (lifetime is not null) streamCts.CancelAfter(lifetime.Value);
        var streamToken = streamCts.Token;

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

            await responseStream.WriteAsync(response, streamToken).ConfigureAwait(false);
            _logger.SnapshotBatchSent(context.Peer, response.SituationObjects.Count);
        }

        // Live events: drain everything available into one batched response to
        // minimize per-message overhead under load.
        try
        {
            var reader = subscription.Reader;
            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                var response = CreateResponse();
                while (response.SituationObjects.Count < batchSize && reader.TryRead(out var obj))
                    response.SituationObjects.Add(obj);

                await responseStream.WriteAsync(response, streamToken).ConfigureAwait(false);
                _logger.EventBatchSent(context.Peer, response.SituationObjects.Count);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client went away - normal for long-lived streams.
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            // Only the injected lifetime can cancel the linked token without the
            // caller's own token having been cancelled first.
            _faults.RecordStreamAborted(context.Peer);
            _logger.StreamAborted(context.Peer);
            throw new RpcException(new Status(
                StatusCode.Unavailable, "Injected fault: stream lifetime elapsed (Simulator:Faults)."));
        }
        finally
        {
            _logger.SubscriberDisconnected(context.Peer);
        }
    }

    private static ResponseHeader InjectedFailure()
    {
        return new ResponseHeader
        {
            Success = false,
            ErrorMessage = "Injected fault: the request was rejected on purpose (Simulator:Faults)."
        };
    }

    private static SubscribeSituationObjectEventsResponse CreateResponse()
    {
        return new SubscribeSituationObjectEventsResponse
        {
            Header = new ResponseHeader { Success = true }
        };
    }
}
