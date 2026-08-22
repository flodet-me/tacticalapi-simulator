using System.Diagnostics.Metrics;

namespace TacticalApi.Simulator.Core.Diagnostics;

/// <summary>
///     The simulator's own <see cref="Meter" /> and every instrument published on
///     it. Built on <c>System.Diagnostics.Metrics</c> - part of the BCL, so this
///     adds no dependency and any OpenTelemetry-based collector can subscribe to
///     <see cref="MeterName" /> if one is ever wired up; the Host additionally
///     renders these instruments itself on <c>/metrics</c> in Prometheus text
///     format (see its <c>PrometheusFormatter</c>).
///     Instruments are deliberately tag-free apart from where a tag carries real
///     information (fault kind, source name), keeping the scrape output small
///     enough to read by eye.
/// </summary>
public sealed class SimulatorMetrics : IDisposable
{
    /// <summary>Meter name every instrument here is published under.</summary>
    public const string MeterName = "TacticalApi.Simulator";

    private readonly Counter<long> _eventsDropped;
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _faultsInjected;
    private readonly Meter _meter;
    private readonly Counter<long> _objectsDeleted;
    private readonly Counter<long> _objectsExpired;
    private readonly Counter<long> _updatesApplied;
    private readonly Counter<long> _updatesRejected;
    private readonly Counter<long> _updatesStale;

    /// <summary>
    ///     The meter every instrument here belongs to.
    ///     Exposed so a listener can filter on this exact instance rather than on
    ///     <see cref="MeterName" />: several simulators can share a process (the E2E
    ///     suite runs many hosts side by side), and name-based filtering would make
    ///     each one's scrape include every other one's measurements.
    /// </summary>
    public Meter Meter => _meter;

    /// <summary>Creates the meter and its instruments.</summary>
    public SimulatorMetrics()
    {
        _meter = new Meter(MeterName);

        _updatesApplied = _meter.CreateCounter<long>(
            "tacticalapi_updates_applied_total", "updates",
            "Situation object updates merged into the store.");
        _updatesStale = _meter.CreateCounter<long>(
            "tacticalapi_updates_stale_total", "updates",
            "Updates discarded because their reporting_time was older than the stored one.");
        _updatesRejected = _meter.CreateCounter<long>(
            "tacticalapi_updates_rejected_total", "updates",
            "Update batches rejected with an error header (missing identity, object cap, ...).");
        _objectsDeleted = _meter.CreateCounter<long>(
            "tacticalapi_objects_deleted_total", "objects",
            "Situation objects marked deleted via DeleteSituationObjects.");
        _objectsExpired = _meter.CreateCounter<long>(
            "tacticalapi_objects_expired_total", "objects",
            "Situation objects marked deleted by the expiry sweeper.");
        _eventsPublished = _meter.CreateCounter<long>(
            "tacticalapi_subscriber_events_published_total", "events",
            "Change events written to subscriber channels (counted once per subscriber).");

        // The instrument this whole file exists for: with the default
        // SubscriberChannelFullMode of DropOldest a slow subscriber silently
        // loses events, and until now nothing anywhere counted that.
        _eventsDropped = _meter.CreateCounter<long>(
            "tacticalapi_subscriber_events_dropped_total", "events",
            "Change events dropped because a subscriber's bounded channel was full.");

        _faultsInjected = _meter.CreateCounter<long>(
            "tacticalapi_faults_injected_total", "faults",
            "Faults injected by the fault-injection layer, tagged by kind.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _meter.Dispose();
    }

    /// <summary>Records the outcome of one AddOrUpdate batch.</summary>
    public void RecordBatch(int applied, int stale)
    {
        if (applied > 0) _updatesApplied.Add(applied);
        if (stale > 0) _updatesStale.Add(stale);
    }

    /// <summary>Records one rejected (error-header) write batch.</summary>
    public void RecordRejectedBatch()
    {
        _updatesRejected.Add(1);
    }

    /// <summary>Records objects marked deleted by an explicit delete request.</summary>
    public void RecordDeleted(int count)
    {
        if (count > 0) _objectsDeleted.Add(count);
    }

    /// <summary>Records objects marked deleted by the expiry sweeper.</summary>
    public void RecordExpired(int count)
    {
        if (count > 0) _objectsExpired.Add(count);
    }

    /// <summary>Records a successful write into one subscriber's channel.</summary>
    public void RecordEventPublished()
    {
        _eventsPublished.Add(1);
    }

    /// <summary>Records an event lost because a subscriber channel overflowed.</summary>
    public void RecordEventDropped()
    {
        _eventsDropped.Add(1);
    }

    /// <summary>Records an injected fault of the given kind (see the Host's fault-injection options).</summary>
    public void RecordFaultInjected(string kind)
    {
        _faultsInjected.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>
    ///     Publishes the current situation object count as an observable gauge.
    ///     Called by <c>SituationStore</c> itself rather than registered centrally,
    ///     so the gauge can't outlive - or go missing from - the object it reports on.
    /// </summary>
    public void RegisterSituationObjectCount(Func<int> observe)
    {
        _meter.CreateObservableGauge(
            "tacticalapi_situation_objects", () => (long)observe(), "objects",
            "Situation objects currently held (including soft-deleted ones).");
    }

    /// <summary>Publishes the current subscriber count as an observable gauge; see <see cref="RegisterSituationObjectCount" />.</summary>
    public void RegisterSubscriberCount(Func<int> observe)
    {
        _meter.CreateObservableGauge(
            "tacticalapi_subscribers", () => (long)observe(), "subscribers",
            "Active SubscribeSituationObjectEvents streams.");
    }
}
