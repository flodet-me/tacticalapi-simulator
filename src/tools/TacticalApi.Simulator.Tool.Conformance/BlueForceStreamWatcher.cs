using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     <see cref="StreamWatcher" /> for <c>SubscribeBlueForceEvents</c> and
///     <c>SubscribePositionChangedEvents</c>.
///     Separate from the situation watcher rather than generic over it: the three
///     streams differ in the field their responses carry (repeated situation
///     objects, repeated blue forces, one position), and a shared generic version
///     would need a selector per call site for no gain over the handful of lines
///     below.
/// </summary>
internal static class BlueForceStreamWatcher
{
    /// <summary>See <see cref="StreamWatcher" />: let the subscription settle before changing anything.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Waits for a blue force matching <paramref name="predicate" /> to arrive on the stream.</summary>
    internal static async Task<bool> WaitForBlueForceAsync(
        ConformanceContext context,
        Func<BlueForce, bool> predicate,
        TimeSpan timeout,
        Func<Task>? afterSubscribed,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.BlueForceClient.SubscribeBlueForceEvents(
            new SubscribeBlueForceEventsRequest(), cancellationToken: timeoutCts.Token);

        var trigger = afterSubscribed is null ? null : TriggerAsync(afterSubscribed, timeoutCts.Token);

        try
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(timeoutCts.Token).ConfigureAwait(false))
                if (response.UpdatedBlueForces.Any(predicate))
                    return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            return false;
        }
        finally
        {
            await ObserveAsync(trigger).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Waits for a position matching <paramref name="predicate" /> to arrive on the stream.</summary>
    internal static async Task<bool> WaitForPositionAsync(
        ConformanceContext context,
        Func<Position, bool> predicate,
        TimeSpan timeout,
        Func<Task>? afterSubscribed,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.OwnPoseClient.SubscribePositionChangedEvents(
            new SubscribePositionEventsRequest(), cancellationToken: timeoutCts.Token);

        var trigger = afterSubscribed is null ? null : TriggerAsync(afterSubscribed, timeoutCts.Token);

        try
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(timeoutCts.Token).ConfigureAwait(false))
                if (response.Position is { } position && predicate(position))
                    return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            return false;
        }
        finally
        {
            await ObserveAsync(trigger).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    ///     Opens a blue force subscription and reports the transport failure if there
    ///     was one. Writes nothing. Timing out is success: an implementation with no
    ///     blue forces legitimately sends no initial snapshot at all.
    /// </summary>
    internal static async Task<string?> TryOpenBlueForcesAsync(
        ConformanceContext context, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.BlueForceClient.SubscribeBlueForceEvents(
            new SubscribeBlueForceEventsRequest(), cancellationToken: timeoutCts.Token);

        return await FirstBadHeaderAsync(
                call.ResponseStream.ReadAllAsync(timeoutCts.Token), response => response.Header)
            .ConfigureAwait(false);
    }

    /// <summary>As <see cref="TryOpenBlueForcesAsync" />, for the position stream.</summary>
    internal static async Task<string?> TryOpenPositionsAsync(
        ConformanceContext context, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.OwnPoseClient.SubscribePositionChangedEvents(
            new SubscribePositionEventsRequest(), cancellationToken: timeoutCts.Token);

        return await FirstBadHeaderAsync(
                call.ResponseStream.ReadAllAsync(timeoutCts.Token), response => response.Header)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads one response and reports its header if unsuccessful. One is enough:
    ///     the stream opened, the server answered, and the header says whether it is
    ///     willing to serve. Waiting out the rest of the probe would only be slow.
    /// </summary>
    private static async Task<string?> FirstBadHeaderAsync<TResponse>(
        IAsyncEnumerable<TResponse> responses, Func<TResponse, ResponseHeader?> headerOf)
    {
        try
        {
            var enumerator = responses.GetAsyncEnumerator();
            await using var _ = enumerator.ConfigureAwait(false);

            if (await enumerator.MoveNextAsync().ConfigureAwait(false)
                && headerOf(enumerator.Current) is { Success: false } header)
                return $"the stream reported header.success = false: {header.ErrorMessage}";
        }
        catch (OperationCanceledException)
        {
            // The timeout elapsed with the stream healthy - nothing was wrong.
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            // As above: our own deadline, not the server's doing.
        }
        catch (RpcException ex)
        {
            return $"{ex.StatusCode} {ex.Status.Detail}";
        }

        return null;
    }

    private static async Task TriggerAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
        await action().ConfigureAwait(false);
    }

    /// <summary>
    ///     Awaits the trigger so its failure is not lost, but never lets that failure
    ///     replace the check's own result - see <see cref="StreamWatcher" />.
    /// </summary>
    private static async Task ObserveAsync(Task? trigger)
    {
        if (trigger is null) return;

        try
        {
            await trigger.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The watch ended before the change was made.
        }
        catch (RpcException)
        {
            // Reported through the check's own result instead.
        }
    }
}
