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
///     gRPC implementation of rheinmetall.tactical_api.v0.BlueForceTracking. Thin
///     layer, exactly like <see cref="SituationGrpcService" />: state handling lives
///     in <see cref="BlueForceStore" /> and this class only translates between RPC
///     messages and the store/broker, plus the faults that are expressed in terms of
///     these RPCs (an unsuccessful response header, a write acknowledged but not
///     applied, a subscription cut short). Latency and RPC-level faults never reach
///     here; see <see cref="FaultInjectionInterceptor" />.
///     Note that the fault switches are shared with the Situation service rather than
///     configured separately: a client integrating against this contract talks to all
///     three services, and a fault run that misbehaved on one of them while the others
///     stayed perfect would be testing a situation that cannot happen to a real server.
/// </summary>
public sealed class BlueForceTrackingGrpcService : BlueForceTracking.BlueForceTrackingBase
{
    private readonly BlueForceEventBroker _broker;
    private readonly FaultInjector _faults;
    private readonly ILogger<BlueForceTrackingGrpcService> _logger;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly BlueForceStore _store;

    /// <summary>Creates the service with its store/broker/config/faults/logging dependencies.</summary>
    public BlueForceTrackingGrpcService(
        BlueForceStore store,
        BlueForceEventBroker broker,
        IOptionsMonitor<SimulatorOptions> options,
        FaultInjector faults,
        ILogger<BlueForceTrackingGrpcService> logger)
    {
        _store = store;
        _broker = broker;
        _options = options;
        _faults = faults;
        _logger = logger;
    }

    /// <summary>Returns every blue force currently available.</summary>
    public override Task<GetBlueForcesResponse> GetBlueForces(
        GetBlueForcesRequest request, ServerCallContext context)
    {
        var response = new GetBlueForcesResponse
        {
            Header = new ResponseHeader { Success = true }
        };
        response.BlueForces.AddRange(_store.GetSnapshot());
        _logger.GetBlueForcesServed(response.BlueForces.Count);
        return Task.FromResult(response);
    }

    /// <summary>
    ///     Adds or updates the given blue forces. Each call is also the keep-alive
    ///     that stops them being implicitly deleted (see
    ///     <see cref="BlueForceTimeoutSweeper" />).
    /// </summary>
    public override Task<AddOrUpdateBlueForcesResponse> AddOrUpdateBlueForces(
        AddOrUpdateBlueForcesRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        _logger.AddOrUpdateBlueForcesReceived(request.BlueForcesToUpdates.Count);

        if (_faults.ShouldReturnErrorHeader(nameof(AddOrUpdateBlueForces)))
            return Task.FromResult(new AddOrUpdateBlueForcesResponse { Header = InjectedFailure() });

        // Acknowledged, never applied. Worth injecting here in particular: a dropped
        // keep-alive doesn't just lose one update, it lets the blue force time out
        // and disappear while its own sender believes it is being tracked.
        if (_faults.ShouldDropWrite(nameof(AddOrUpdateBlueForces)))
            return Task.FromResult(new AddOrUpdateBlueForcesResponse
            {
                Header = new ResponseHeader { Success = true }
            });

        var result = _store.AddOrUpdate(request.BlueForcesToUpdates);
        if (!result.Success) _logger.AddOrUpdateBlueForcesFailed(result.ErrorMessage);
        return Task.FromResult(new AddOrUpdateBlueForcesResponse { Header = result.ToHeader() });
    }

    /// <summary>
    ///     Streams every existing blue force ("Initially, all existing blue forces
    ///     are returned for every call") and then live changes until the client
    ///     disconnects.
    /// </summary>
    public override async Task SubscribeBlueForceEvents(
        SubscribeBlueForceEventsRequest request,
        IServerStreamWriter<SubscribeBlueForceEventsResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var batchSize = _options.CurrentValue.Performance.StreamBatchSize;

        // Fixed at subscription time and driven by a timer on a linked token, for
        // the same reasons as the situation stream - see SituationGrpcService.
        var lifetime = _faults.StreamLifetime();
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (lifetime is not null) streamCts.CancelAfter(lifetime.Value);
        var streamToken = streamCts.Token;

        using var scope = _logger.BeginScope("Peer={Peer}", context.Peer);
        _logger.BlueForceSubscriberConnected(context.Peer);

        // Subscribe BEFORE taking the snapshot so no change is lost in between. A
        // blue force updated during the snapshot may be delivered twice, which is
        // harmless because every update carries the blue force whole.
        using var subscription = _broker.Subscribe();

        var snapshot = _store.GetSnapshot();
        for (var offset = 0; offset < snapshot.Count; offset += batchSize)
        {
            var response = CreateResponse();
            for (var i = offset; i < Math.Min(offset + batchSize, snapshot.Count); i++)
                response.UpdatedBlueForces.Add(snapshot[i]);

            await responseStream.WriteAsync(response, streamToken).ConfigureAwait(false);
            _logger.BlueForceBatchSent(context.Peer, response.UpdatedBlueForces.Count);
        }

        try
        {
            var reader = subscription.Reader;
            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                var response = CreateResponse();
                while (response.UpdatedBlueForces.Count < batchSize && reader.TryRead(out var blueForce))
                    response.UpdatedBlueForces.Add(blueForce);

                await responseStream.WriteAsync(response, streamToken).ConfigureAwait(false);
                _logger.BlueForceBatchSent(context.Peer, response.UpdatedBlueForces.Count);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client went away - normal for long-lived streams.
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            _faults.RecordStreamAborted(context.Peer);
            _logger.StreamAborted(context.Peer);
            throw new RpcException(new Status(
                StatusCode.Unavailable, "Injected fault: stream lifetime elapsed (Simulator:Faults)."));
        }
        finally
        {
            _logger.BlueForceSubscriberDisconnected(context.Peer);
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

    private static SubscribeBlueForceEventsResponse CreateResponse()
    {
        return new SubscribeBlueForceEventsResponse
        {
            Header = new ResponseHeader { Success = true }
        };
    }
}
