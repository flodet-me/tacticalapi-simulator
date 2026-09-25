using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Diagnostics;

namespace TacticalApi.Simulator.Core.Events;

/// <summary>
///     Fan-out of situation object changes to <c>SubscribeSituationObjectEvents</c>
///     subscribers. The channel mechanics live in <see cref="EventBroker{T}" />;
///     what this type adds is the pair of counters those drops and writes land on.
/// </summary>
public sealed class SituationEventBroker : EventBroker<SituationObject>
{
    private readonly SimulatorMetrics _metrics;

    /// <summary>Creates the broker and publishes its subscriber-count gauge.</summary>
    public SituationEventBroker(IOptionsMonitor<SimulatorOptions> options, SimulatorMetrics metrics)
        : base(options)
    {
        _metrics = metrics;
        _metrics.RegisterSubscriberCount(() => SubscriberCount);
    }

    /// <inheritdoc/>
    protected override void RecordDropped()
    {
        _metrics.RecordEventDropped();
    }

    /// <inheritdoc/>
    protected override void RecordPublished()
    {
        _metrics.RecordEventPublished();
    }
}
