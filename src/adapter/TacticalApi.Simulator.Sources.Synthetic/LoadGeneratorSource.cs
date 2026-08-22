using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Synthetic.Logging;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>
///     Moves a configurable number of objects every cycle, as fast as configured, to
///     put an endpoint under real load.
///     This is the other half of the metrics work: <c>Simulator:Performance</c> has
///     always had knobs (subscriber channel capacity, full mode, batch size, object
///     cap) with no way to reach the conditions they govern. Running this against the
///     Host with a subscriber attached is what makes
///     <c>tacticalapi_subscriber_events_dropped_total</c> move, and therefore what
///     makes those knobs tunable rather than guessable.
///     Positions are a deterministic function of the object index and elapsed time -
///     no RNG - so two runs with the same settings produce the same load, and a
///     throughput comparison between them measures the change under test rather than
///     the weather.
/// </summary>
public sealed class LoadGeneratorSource(
    IOptionsMonitor<LoadGeneratorOptions> options,
    TimeProvider timeProvider,
    ILogger<LoadGeneratorSource> logger)
    : ISimulationSource
{
    private readonly DateTimeOffset _epoch = timeProvider.GetUtcNow();
    private int _windowStart;

    /// <inheritdoc/>
    public string Name => LoadGeneratorOptions.Name;

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    /// <summary>
    ///     Produces the next window of <see cref="LoadGeneratorOptions.BatchSize" />
    ///     objects, advancing through the population of
    ///     <see cref="LoadGeneratorOptions.ObjectCount" /> a window per cycle and
    ///     wrapping around at the end.
    ///     Reporting a rolling subset rather than the whole population every cycle is
    ///     both what real sources do and what makes the two knobs independent: batch
    ///     size sets how big each message is, the interval sets how often one is sent,
    ///     and the object count sets how large the situation grows - which would
    ///     otherwise be capped by whatever fits in a single message.
    /// </summary>
    public Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var elapsed = (now - _epoch).TotalSeconds;

        // A square grid keeps every object at a distinct, stable position, so the map
        // UI shows the load as a block rather than a single overlapping dot.
        var side = (int)Math.Ceiling(Math.Sqrt(settings.ObjectCount));
        var step = settings.SpreadDegrees * 2 / side;

        var batchSize = Math.Min(settings.BatchSize, settings.ObjectCount);
        var start = _windowStart % settings.ObjectCount;
        _windowStart = (start + batchSize) % settings.ObjectCount;

        var updates = new List<UpdateSituationObject>(batchSize);
        for (var offset = 0; offset < batchSize; offset++)
        {
            var i = (start + offset) % settings.ObjectCount;
            var row = i / side;
            var column = i % side;

            // A slow shared drift means every object changes every cycle - a load
            // generator whose objects never move would be merged away as a no-op
            // update by any correct implementation, and would measure nothing.
            var drift = Math.Sin(elapsed / 10.0 + i * 0.001) * step * 0.5;

            var track = new TrackReport(
                $"load:{i:D7}",
                $"LOAD{i:D7}",
                settings.CenterLatitude - settings.SpreadDegrees + row * step + drift,
                settings.CenterLongitude - settings.SpreadDegrees + column * step + drift,
                null,
                null,
                null,
                null);

            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, settings.ReporterId, settings.SymbolCode, settings.SymbolCatalog, now,
                settings.TrackTimeToLive));
        }

        logger.LoadObjectsProduced(updates.Count);
        return Task.FromResult<IReadOnlyList<UpdateSituationObject>>(updates);
    }
}
