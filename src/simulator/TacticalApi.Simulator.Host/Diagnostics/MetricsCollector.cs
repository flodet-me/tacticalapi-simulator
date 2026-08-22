using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TacticalApi.Simulator.Core.Diagnostics;

namespace TacticalApi.Simulator.Host.Diagnostics;

/// <summary>
///     Subscribes to the simulator's own <see cref="Meter" /> and keeps the current
///     value of every instrument, so <c>/metrics</c> can render them.
///     A <see cref="MeterListener" /> rather than an OpenTelemetry exporter on
///     purpose: the whole point of the metrics work was to stop
///     <c>SubscriberChannelFullMode</c> dropping events unobserved, and that goal
///     doesn't justify pulling a collector stack (and its licence and CVE surface -
///     both of which this repo's CI checks) into an image that otherwise depends on
///     nothing but gRPC. Anyone who does want OTLP can point a collector at
///     <see cref="SimulatorMetrics.MeterName" /> without changing a line here,
///     because it's a plain BCL meter.
///     Counters are accumulated from their increments; observable instruments are
///     polled on demand when a scrape arrives.
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    private readonly ConcurrentDictionary<string, MetricSeries> _series = new(StringComparer.Ordinal);
    private readonly MeterListener _listener;

    /// <summary>Starts listening to this simulator's meter.</summary>
    public MetricsCollector(SimulatorMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                // By instance, not by name: MeterListener.Start below also replays
                // instruments that already exist, so name filtering would pick up
                // every other simulator sharing this process.
                if (!ReferenceEquals(instrument.Meter, metrics.Meter)) return;

                listener.EnableMeasurementEvents(instrument);

                // Seed the series at zero so a counter that hasn't fired yet still
                // appears in a scrape. A metric that only materialises once something
                // has gone wrong is the one nobody has an alert on, because it wasn't
                // there when the alert was written.
                Seed(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _listener.Dispose();
    }

    /// <summary>
    ///     Polls the observable instruments and returns every series currently known,
    ///     ordered by name so a scrape is stable and diffable between calls.
    /// </summary>
    public IReadOnlyList<MetricSeries> Collect()
    {
        _listener.RecordObservableInstruments();
        return _series.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Labels,
            StringComparer.Ordinal).ToList();
    }

    private void Seed(Instrument instrument)
    {
        _series.TryAdd(
            instrument.Name,
            new MetricSeries(instrument.Name, string.Empty, instrument.Description, instrument is Counter<long>, 0));
    }

    private void OnMeasurement(
        Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var labels = FormatLabels(tags);
        var key = $"{instrument.Name}{labels}";
        var isCounter = instrument is Counter<long>;

        // Once an instrument reports with labels, its unlabelled seed is meaningless -
        // the real series are the labelled ones, and leaving a bare zero beside them
        // would suggest a category that doesn't exist.
        if (labels.Length > 0) _series.TryRemove(instrument.Name, out _);

        _series.AddOrUpdate(
            key,
            _ => new MetricSeries(instrument.Name, labels, instrument.Description, isCounter, measurement),
            // A counter reports each increment, a gauge its current value; the same
            // callback serves both, so which one it is decides whether to add or replace.
            (_, existing) => existing with { Value = isCounter ? existing.Value + measurement : measurement });
    }

    private static string FormatLabels(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.Length == 0) return string.Empty;

        var parts = new List<string>(tags.Length);
        foreach (var tag in tags) parts.Add($"{tag.Key}=\"{tag.Value}\"");

        return $"{{{string.Join(",", parts)}}}";
    }
}

/// <summary>One metric series: an instrument plus one particular combination of tag values.</summary>
/// <param name="Name">Instrument name, used verbatim as the Prometheus metric name.</param>
/// <param name="Labels">Rendered Prometheus label set, including braces, or empty.</param>
/// <param name="Description">Instrument description, rendered as HELP.</param>
/// <param name="IsCounter">Whether this is a monotonic counter (as opposed to a gauge).</param>
/// <param name="Value">Current value.</param>
public sealed record MetricSeries(
    string Name, string Labels, string? Description, bool IsCounter, long Value);
