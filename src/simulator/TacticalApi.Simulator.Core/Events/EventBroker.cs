using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Events;

/// <summary>
///     Fan-out of changes to streaming subscribers, built on
///     System.Threading.Channels. Each subscriber gets its own bounded channel;
///     capacity and overflow behavior come from <see cref="PerformanceOptions" />
///     and are read per subscription, so config changes apply to new subscribers
///     without a restart (IOptionsMonitor).
///     The contract has three services and two of them stream (situation object
///     events, blue force events) plus a third that streams a single value
///     (own position), and the channel mechanics are identical for all of them -
///     what differs is only which counters the drops and writes land on, which is
///     what the two hooks below are for.
/// </summary>
/// <typeparam name="T">What one streamed change carries.</typeparam>
public abstract class EventBroker<T>
{
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly ConcurrentDictionary<Guid, Channel<T>> _subscribers = new();

    /// <summary>Creates a broker reading its channel settings from <paramref name="options" />.</summary>
    protected EventBroker(IOptionsMonitor<SimulatorOptions> options)
    {
        _options = options;
    }

    /// <summary>Number of active subscriptions.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>Counts one change lost because a subscriber's channel was full.</summary>
    protected abstract void RecordDropped();

    /// <summary>Counts one change successfully written into a subscriber's channel.</summary>
    protected abstract void RecordPublished();

    /// <summary>Opens a new subscriber channel; dispose the returned handle to unsubscribe.</summary>
    public Subscription Subscribe()
    {
        var perf = _options.CurrentValue.Performance;

        // The itemDropped callback is the only way to observe a DropOldest/DropWrite
        // discard at all: TryWrite still returns true when the channel silently threw
        // an older item away, so without this the default full-mode loses events with
        // nothing anywhere to show for it.
        var channel = Channel.CreateBounded(
            new BoundedChannelOptions(perf.SubscriberChannelCapacity)
            {
                FullMode = perf.SubscriberChannelFullMode,
                SingleReader = true,
                SingleWriter = false
            },
            (T _) => RecordDropped());

        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return new Subscription(this, id, channel.Reader);
    }

    /// <summary>Fans out a batch of changes to every current subscriber.</summary>
    public void Publish(IReadOnlyList<T> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (_subscribers.IsEmpty || changed.Count == 0) return;

        // S3267 (suggests a LINQ .Where() here) doesn't fit: TryWrite is the write
        // itself, not a side-effect-free predicate, so it can't be pulled into a
        // filter without changing what the loop does.
#pragma warning disable S3267
        foreach (var (_, channel) in _subscribers)
            foreach (var item in changed)
            {
                // With DropOldest/DropWrite this never blocks; with Wait mode a
                // full channel makes TryWrite fail and we fall back to a
                // blocking write to apply backpressure to the publisher.
                if (!channel.Writer.TryWrite(item))
                {
                    var writeTask = channel.Writer.WriteAsync(item);
                    if (!writeTask.IsCompletedSuccessfully) writeTask.AsTask().GetAwaiter().GetResult();
                }

                RecordPublished();
            }
#pragma warning restore S3267
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    /// <summary>Disposable handle owning one subscriber channel.</summary>
    public sealed class Subscription : IDisposable
    {
        private readonly EventBroker<T> _broker;
        private readonly Guid _id;

        internal Subscription(EventBroker<T> broker, Guid id, ChannelReader<T> reader)
        {
            _broker = broker;
            _id = id;
            Reader = reader;
        }

        /// <summary>Read side of this subscriber's channel.</summary>
        public ChannelReader<T> Reader { get; }

        /// <summary>Unsubscribes and completes the underlying channel.</summary>
        public void Dispose()
        {
            _broker.Unsubscribe(_id);
        }
    }
}
