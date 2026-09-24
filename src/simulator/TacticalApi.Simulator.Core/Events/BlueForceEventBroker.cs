using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Diagnostics;

namespace TacticalApi.Simulator.Core.Events;

/// <summary>
///     Fan-out of blue force changes to <c>SubscribeBlueForceEvents</c> subscribers.
///     Counted on its own instruments rather than the situation ones: the two
///     streams carry different traffic at very different rates (a blue force
///     re-reports itself on a keep-alive cadence, situation objects change when
///     something happens), and adding them together would make either number
///     useless on its own.
/// </summary>
public sealed class BlueForceEventBroker : EventBroker<BlueForce>
{
    private readonly SimulatorMetrics _metrics;

    /// <summary>Creates the broker and publishes its subscriber-count gauge.</summary>
    public BlueForceEventBroker(IOptionsMonitor<SimulatorOptions> options, SimulatorMetrics metrics)
        : base(options)
    {
        _metrics = metrics;
        _metrics.RegisterBlueForceSubscriberCount(() => SubscriberCount);
    }

    /// <inheritdoc/>
    protected override void RecordDropped()
    {
        _metrics.RecordBlueForceEventDropped();
    }

    /// <inheritdoc/>
    protected override void RecordPublished()
    {
        _metrics.RecordBlueForceEventPublished();
    }
}
