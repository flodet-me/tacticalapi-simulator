using Grpc.Core;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Faults;
using TacticalApi.Simulator.Host.Logging;

namespace TacticalApi.Simulator.Host.Services;

/// <summary>
///     gRPC implementation of rheinmetall.tactical_api.v0.OwnPose. Thin layer over
///     <see cref="OwnPoseStore" />, with the same RPC-level faults as the other two
///     services (see <see cref="BlueForceTrackingGrpcService" />).
///     The stream carries no batching: unlike situation objects and blue forces
///     there is only ever one primary position, so a batch would always hold exactly
///     one element and <c>SubscribePositionEventsResponse</c> is shaped accordingly -
///     a single <c>position</c> field, not a repeated one.
/// </summary>
public sealed class OwnPoseGrpcService : OwnPose.OwnPoseBase
{
    private readonly PositionEventBroker _broker;
    private readonly FaultInjector _faults;
    private readonly ILogger<OwnPoseGrpcService> _logger;
    private readonly OwnPoseStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the service with its store/broker/faults/clock/logging dependencies.</summary>
    public OwnPoseGrpcService(
        OwnPoseStore store,
        PositionEventBroker broker,
        FaultInjector faults,
        TimeProvider timeProvider,
        ILogger<OwnPoseGrpcService> logger)
    {
        _store = store;
        _broker = broker;
        _faults = faults;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    ///     Returns the position currently selected as primary. A successful response
    ///     with no position at all is the honest answer before any source has
    ///     reported - the contract allows the position to be absent ("Might be null
    ///     if unset or invalid"), and failing the call would say something different
    ///     and wrong, namely that the request was bad.
    /// </summary>
    public override Task<GetPositionResponse> GetPosition(GetPositionRequest request, ServerCallContext context)
    {
        var response = new GetPositionResponse
        {
            Header = new ResponseHeader { Success = true },
            Position = _store.GetPosition(_timeProvider.GetUtcNow())
        };
        _logger.GetPositionServed(response.Position?.SourceIdentifier ?? "(none)");
        return Task.FromResult(response);
    }

    /// <summary>Records the given position source's current fix.</summary>
    public override Task<UpdatePositionResponse> UpdatePosition(
        UpdatePositionRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        _logger.UpdatePositionReceived(request.Position?.SourceIdentifier ?? "(unset)");

        if (_faults.ShouldReturnErrorHeader(nameof(UpdatePosition)))
            return Task.FromResult(new UpdatePositionResponse { Header = InjectedFailure() });

        // Acknowledged, never applied - which, given the staleness rules, is how a
        // client ends up watching its own position expire while every call succeeds.
        if (_faults.ShouldDropWrite(nameof(UpdatePosition)))
            return Task.FromResult(new UpdatePositionResponse
            {
                Header = new ResponseHeader { Success = true }
            });

        var result = _store.UpdatePosition(request.Position, _timeProvider.GetUtcNow());
        if (!result.Success) _logger.UpdatePositionFailed(result.ErrorMessage);
        return Task.FromResult(new UpdatePositionResponse { Header = result.ToHeader() });
    }

    /// <summary>
    ///     Streams the current position ("Initially, the current position is
    ///     returned") and then every subsequent change to it, including the change
    ///     from valid to expired that happens when no updates arrive at all.
    /// </summary>
    public override async Task SubscribePositionChangedEvents(
        SubscribePositionEventsRequest request,
        IServerStreamWriter<SubscribePositionEventsResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var lifetime = _faults.StreamLifetime();
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (lifetime is not null) streamCts.CancelAfter(lifetime.Value);
        var streamToken = streamCts.Token;

        using var scope = _logger.BeginScope("Peer={Peer}", context.Peer);
        _logger.PositionSubscriberConnected(context.Peer);

        // Subscribe before reading the current position so a change in between is
        // still delivered; a duplicate is harmless, a gap would not be.
        using var subscription = _broker.Subscribe();

        if (_store.GetPosition(_timeProvider.GetUtcNow()) is { } current)
            await responseStream.WriteAsync(CreateResponse(current), streamToken).ConfigureAwait(false);

        try
        {
            var reader = subscription.Reader;
            await foreach (var position in reader.ReadAllAsync(streamToken).ConfigureAwait(false))
                await responseStream.WriteAsync(CreateResponse(position), streamToken).ConfigureAwait(false);
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
            _logger.PositionSubscriberDisconnected(context.Peer);
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

    private static SubscribePositionEventsResponse CreateResponse(Position position)
    {
        return new SubscribePositionEventsResponse
        {
            Header = new ResponseHeader { Success = true },
            Position = position
        };
    }
}
