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

    public int SubscriberCount => _subscribers.Count;

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

    public void Publish(IReadOnlyList<SituationObject> changed)
    {
        if (_subscribers.IsEmpty) return;

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

        public ChannelReader<SituationObject> Reader { get; }

        public void Dispose()
        {
            _broker.Unsubscribe(_id);
        }
    }
}
