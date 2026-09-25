using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Diagnostics;

namespace TacticalApi.Simulator.Core.Events;

/// <summary>
///     Fan-out of own-position changes to <c>SubscribePositionChangedEvents</c>
///     subscribers. Only the position selected as primary is ever published here -
///     the contract's stream is explicitly "the one selected as primary position
///     source by the application", not every sensor's own view (see
///     <see cref="TacticalApi.Simulator.Core.Store.OwnPoseStore" />).
/// </summary>
public sealed class PositionEventBroker : EventBroker<Position>
{
    private readonly SimulatorMetrics _metrics;

    /// <summary>Creates the broker and publishes its subscriber-count gauge.</summary>
    public PositionEventBroker(IOptionsMonitor<SimulatorOptions> options, SimulatorMetrics metrics)
        : base(options)
    {
        _metrics = metrics;
        _metrics.RegisterPositionSubscriberCount(() => SubscriberCount);
    }

    /// <inheritdoc/>
    protected override void RecordDropped()
    {
        _metrics.RecordPositionEventDropped();
    }

    /// <inheritdoc/>
    protected override void RecordPublished()
    {
        _metrics.RecordPositionEventPublished();
    }
}
