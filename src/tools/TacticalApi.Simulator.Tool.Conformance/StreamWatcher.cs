using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Opens a subscription, optionally performs an action once it is established,
///     and waits for an object matching a predicate to arrive - the shape every
///     streaming check needs.
///     The action runs after subscribing rather than before, which is what separates
///     "was already there when I subscribed" from "happened while I was watching".
///     It is fired on a short timer rather than off the first received batch: the
///     contract's initial snapshot of an empty situation is zero messages, so waiting
///     for one would hang forever exactly when the situation is empty - which is the
///     normal state of a freshly started simulator.
/// </summary>
internal static class StreamWatcher
{
    /// <summary>
    ///     How long to let the subscription settle before making the change the check
    ///     is watching for. Generous enough to cover a real network round-trip, since
    ///     the alternative - triggering too early - makes the check pass for the wrong
    ///     reason rather than fail.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    internal static async Task<bool> WaitForAsync(
        ConformanceContext context,
        Func<SituationObject, bool> predicate,
        TimeSpan timeout,
        Func<Task>? afterSubscribed,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.Client.SubscribeSituationObjectEvents(
            new SubscribeSituationObjectEventsRequest(), cancellationToken: timeoutCts.Token);

        var trigger = afterSubscribed is null ? null : TriggerAsync(afterSubscribed, timeoutCts.Token);

        try
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(timeoutCts.Token).ConfigureAwait(false))
                if (response.SituationObjects.Any(predicate))
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

    private static async Task TriggerAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
        await action().ConfigureAwait(false);
    }

    /// <summary>
    ///     Awaits the trigger so its failure is not lost, but never lets that failure
    ///     replace the check's own result - if the write failed, the check is already
    ///     going to report that nothing arrived, which is the more useful message.
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

    /// <summary>
    ///     Opens a subscription, reads whatever arrives until the timeout, and reports
    ///     the transport failure if there was one. Writes nothing, which is what makes
    ///     it safe to point at a situation somebody is relying on.
    ///     Returns null on success. Timing out is success here: an empty situation
    ///     legitimately sends no initial snapshot at all, so "saw an object" would be a
    ///     property of the data rather than of the implementation.
    /// </summary>
    internal static async Task<string?> TryOpenAsync(
        ConformanceContext context, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = context.Client.SubscribeSituationObjectEvents(
            new SubscribeSituationObjectEventsRequest(), cancellationToken: timeoutCts.Token);

        try
        {
            // One response is all this needs: the stream opened, the server answered,
            // and the header says so. Waiting out the rest of the probe would only be
            // slow. An empty situation legitimately sends nothing at all, and runs to
            // the timeout instead - which is also a pass.
            var enumerator = call.ResponseStream.ReadAllAsync(timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);
            await using var _ = enumerator.ConfigureAwait(false);

            if (await enumerator.MoveNextAsync().ConfigureAwait(false) &&
                enumerator.Current.Header is { Success: false } header)
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

    /// <summary>
    ///     Watches the stream across a change and returns the error carried by the
    ///     first unsuccessful response header, or null if every response was fine.
    ///     Worth its own check because <c>SubscribeSituationObjectEventsResponse</c>
    ///     carries a header on every single message, and an implementation that never
    ///     populates it - or populates it with success = false while streaming real
    ///     data - is one a careful client would refuse to trust.
    /// </summary>
    internal static async Task<string?> FindBadHeaderAsync(
        ConformanceContext context, Func<Task> afterSubscribed, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(context.StreamTimeout);

        using var call = context.Client.SubscribeSituationObjectEvents(
            new SubscribeSituationObjectEventsRequest(), cancellationToken: timeoutCts.Token);

        var trigger = TriggerAsync(afterSubscribed, timeoutCts.Token);

        try
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                if (response.Header is null) return "a streamed response carried no header at all";
                if (!response.Header.Success) return response.Header.ErrorMessage ?? "(no error_message)";

                // One good batch after the change is enough; the check is about the
                // header, not about what the batch contains.
                if (response.SituationObjects.Count > 0) return null;
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing bad seen before the timeout.
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            // As above.
        }
        finally
        {
            await ObserveAsync(trigger).ConfigureAwait(false);
        }

        return null;
    }
}
