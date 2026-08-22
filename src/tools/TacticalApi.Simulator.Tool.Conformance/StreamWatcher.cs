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
}
