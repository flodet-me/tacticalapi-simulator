using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Events;

/// <summary>
///     Fan-out of situation object changes to streaming subscribers, built on
///     System.Threading.Channels. Each subscriber gets its own bounded channel;
///     capacity and overflow behavior come from <see cref="PerformanceOptions" />
///     and are read per subscription, so config changes apply to new subscribers
///     without a restart (IOptionsMonitor).
/// </summary>
public sealed class SituationEventBroker(IOptionsMonitor<SimulatorOptions> options)
{
    private readonly ConcurrentDictionary<Guid, Channel<SituationObject>> _subscribers = new();

    /// <summary>Number of active subscriptions.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>Opens a new subscriber channel; dispose the returned handle to unsubscribe.</summary>
    public Subscription Subscribe()
    {
        var perf = options.CurrentValue.Performance;
        var channel = Channel.CreateBounded<SituationObject>(new BoundedChannelOptions(perf.SubscriberChannelCapacity)
        {
            FullMode = perf.SubscriberChannelFullMode,
            SingleReader = true,
            SingleWriter = false
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return new Subscription(this, id, channel.Reader);
    }

    /// <summary>Fans out a batch of changed objects to every current subscriber.</summary>
    public void Publish(IReadOnlyList<SituationObject> changed)
    {
        if (_subscribers.IsEmpty) return;

        // S3267 (suggests a LINQ .Where() here) doesn't fit: TryWrite is the write
        // itself, not a side-effect-free predicate, so it can't be pulled into a
        // filter without changing what the loop does.
#pragma warning disable S3267
        foreach (var (_, channel) in _subscribers)
            foreach (var obj in changed)
                // With DropOldest/DropWrite this never blocks; with Wait mode a
                // full channel makes TryWrite fail and we fall back to a
                // blocking write to apply backpressure to the publisher.
                if (!channel.Writer.TryWrite(obj))
                {
                    var writeTask = channel.Writer.WriteAsync(obj);
                    if (!writeTask.IsCompletedSuccessfully) writeTask.AsTask().GetAwaiter().GetResult();
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
        private readonly SituationEventBroker _broker;
        private readonly Guid _id;

        internal Subscription(SituationEventBroker broker, Guid id, ChannelReader<SituationObject> reader)
        {
            _broker = broker;
            _id = id;
            Reader = reader;
        }

        /// <summary>Read side of this subscriber's channel.</summary>
        public ChannelReader<SituationObject> Reader { get; }

        /// <summary>Unsubscribes and completes the underlying channel.</summary>
        public void Dispose()
        {
            _broker.Unsubscribe(_id);
        }
    }
}
