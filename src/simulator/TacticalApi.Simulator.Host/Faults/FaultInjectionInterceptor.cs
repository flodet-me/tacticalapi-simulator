using Grpc.Core;
using Grpc.Core.Interceptors;

namespace TacticalApi.Simulator.Host.Faults;

/// <summary>
///     Applies the transport-level faults - artificial latency and outright RPC
///     failures - to every call, unary and streaming alike.
///     An interceptor rather than code in the service because these faults have
///     nothing to do with what any particular RPC means: they are things that happen
///     to a call, so they belong where calls pass through. The faults that DO depend
///     on the RPC's semantics (an unsuccessful response header, a silently dropped
///     write, a stream cut short) live in <c>SituationGrpcService</c>, which is the
///     only place that knows what those words mean.
/// </summary>
public sealed class FaultInjectionInterceptor(FaultInjector faults) : Interceptor
{
    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        await ApplyAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        // Latency here delays the stream opening, not each message on it - a client
        // that handles a slow connect still has to handle a slow first snapshot.
        await ApplyAsync(context).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    private async Task ApplyAsync(ServerCallContext context)
    {
        if (!faults.IsEnabled) return;

        var method = context.Method;
        await faults.ApplyLatencyAsync(method, context.CancellationToken).ConfigureAwait(false);
        faults.ThrowIfRpcFaultDrawn(method);
    }
}
