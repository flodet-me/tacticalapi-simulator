using System.Diagnostics.Metrics;
using System.Threading.Channels;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Diagnostics;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for the simulator's metrics
///     (src/simulator/TacticalApi.Simulator.Core/Diagnostics/SimulatorMetrics.cs) and
///     for the drop accounting they exist to make visible.
/// </summary>
public sealed class SimulatorMetricsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public void AddOrUpdate_CountsAppliedAndStaleUpdates()
    {
        // Arrange
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var store = TestHelpers.CreateStore(metrics: metrics);

        // Act
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddHours(1), "LATER")]);
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "EARLIER")]);

        // Assert
        Assert.Equal(1, recorder.Total("tacticalapi_updates_applied_total"));
        Assert.Equal(1, recorder.Total("tacticalapi_updates_stale_total"));
    }

    [Fact]
    public void AddOrUpdate_CountsARejectedBatch()
    {
        // Arrange
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var store = TestHelpers.CreateStore(metrics: metrics);

        // Act - no identity at all, so the whole batch is refused.
        store.AddOrUpdate([
            new Rheinmetall.TacticalApi.V0.UpdateSituationObject
            {
                Symbol = new Rheinmetall.TacticalApi.V0.UpdateSymbol
                {
                    Reporter = new Rheinmetall.TacticalApi.V0.Identity { StringIdentity = TestHelpers.TestReporterId },
                    ReportingTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(T0)
                }
            }
        ]);

        // Assert
        Assert.Equal(1, recorder.Total("tacticalapi_updates_rejected_total"));
    }

    [Fact]
    public void Delete_And_Expiry_AreCountedSeparately()
    {
        // Arrange - the two ways an object leaves the situation are different events
        // operationally, so they get different counters.
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var store = TestHelpers.CreateStore(metrics: metrics);
        store.AddOrUpdate([
            TestHelpers.SymbolUpdate("track-1", T0),
            TestHelpers.SymbolUpdate("track-2", T0, expiry: T0.AddMinutes(1))
        ]);

        // Act
        store.Delete([TestHelpers.Delete("track-1", T0.AddMinutes(2))]);
        store.SweepExpired(T0.AddHours(1), TestHelpers.TestReporterId);

        // Assert - the sweep deletes too, so deletes counts both; expiry counts only its own.
        Assert.Equal(2, recorder.Total("tacticalapi_objects_deleted_total"));
        Assert.Equal(1, recorder.Total("tacticalapi_objects_expired_total"));
    }

    [Fact]
    public void Publish_CountsEventsDroppedByAFullSubscriberChannel()
    {
        // Arrange - the reason this whole file exists: with DropOldest, TryWrite still
        // reports success while quietly discarding an older event, so before these
        // counters a slow subscriber lost data with nothing anywhere to show for it.
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var options = new SimulatorOptions
        {
            Performance = new PerformanceOptions
            {
                SubscriberChannelCapacity = 2,
                SubscriberChannelFullMode = BoundedChannelFullMode.DropOldest
            }
        };
        var broker = TestHelpers.CreateBroker(options, metrics);
        var store = TestHelpers.CreateStore(options, broker, metrics: metrics);

        // A subscriber that never reads: the channel fills after two events.
        using var subscription = broker.Subscribe();

        // Act - five objects into a two-slot channel.
        store.AddOrUpdate([
            TestHelpers.SymbolUpdate("track-1", T0),
            TestHelpers.SymbolUpdate("track-2", T0),
            TestHelpers.SymbolUpdate("track-3", T0),
            TestHelpers.SymbolUpdate("track-4", T0),
            TestHelpers.SymbolUpdate("track-5", T0)
        ]);

        // Assert
        Assert.Equal(5, recorder.Total("tacticalapi_subscriber_events_published_total"));
        Assert.Equal(3, recorder.Total("tacticalapi_subscriber_events_dropped_total"));
    }

    [Fact]
    public void Publish_DropsNothingWhenTheSubscriberKeepsUp()
    {
        // Arrange
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var broker = TestHelpers.CreateBroker(metrics: metrics);
        var store = TestHelpers.CreateStore(broker: broker, metrics: metrics);
        using var subscription = broker.Subscribe();

        // Act
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        // Assert
        Assert.Equal(0, recorder.Total("tacticalapi_subscriber_events_dropped_total"));
    }

    [Fact]
    public void Gauges_ReportTheCurrentSituationSize()
    {
        // Arrange
        using var metrics = new SimulatorMetrics();
        using var recorder = new MeasurementRecorder(metrics);
        var broker = TestHelpers.CreateBroker(metrics: metrics);
        var store = TestHelpers.CreateStore(broker: broker, metrics: metrics);
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);
        using var subscription = broker.Subscribe();

        // Act
        recorder.PollObservables();

        // Assert
        Assert.Equal(1, recorder.Total("tacticalapi_situation_objects"));
        Assert.Equal(1, recorder.Total("tacticalapi_subscribers"));
    }

    /// <summary>
    ///     Collects measurements from one SimulatorMetrics instance for the duration
    ///     of a test. Filtering by instance is what keeps concurrently running test
    ///     classes - each with a store of its own - from seeing each other's numbers.
    ///     MeterListener.Start replays instruments that already exist, so the recorder
    ///     can be created after the metrics it watches.
    /// </summary>
    private sealed class MeasurementRecorder : IDisposable
    {
        private readonly Dictionary<string, long> _totals = new(StringComparer.Ordinal);
        private readonly MeterListener _listener;
        private readonly Lock _gate = new();

        public MeasurementRecorder(SimulatorMetrics metrics)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, metrics.Meter)) listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            {
                lock (_gate)
                {
                    _totals[instrument.Name] =
                        instrument is Counter<long> && _totals.TryGetValue(instrument.Name, out var running)
                            ? running + measurement
                            : measurement;
                }
            });

            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        public void PollObservables()
        {
            _listener.RecordObservableInstruments();
        }

        public long Total(string instrument)
        {
            lock (_gate)
            {
                return _totals.GetValueOrDefault(instrument);
            }
        }
    }
}
