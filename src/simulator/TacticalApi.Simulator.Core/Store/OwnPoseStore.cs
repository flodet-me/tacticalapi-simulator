using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Control;
using TacticalApi.Simulator.Core.Diagnostics;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Runtime-only own-position state, behind the <c>OwnPose</c> service.
///     The service takes updates per position source ("Can be used to update the
///     position of a given position source") but only ever hands back one: "the one
///     selected as primary position source by the application". So this keeps every
///     source's latest fix and applies that selection on the way out - which source
///     is primary comes from <c>Simulator:OwnPose:PrimarySource</c>, defaulting to
///     whichever reported most recently.
///     Staleness is modelled rather than ignored, because the contract spells the
///     case out: a fix that has stopped being refreshed keeps its coordinates and
///     gains <c>is_invalid_or_expired</c>, "because the user entered a building".
///     <see cref="OwnPoseStalenessSweeper" /> is what makes that flip visible to a
///     subscriber who is not receiving updates at all.
/// </summary>
public sealed class OwnPoseStore
{
    private readonly PositionEventBroker _broker;
    private readonly ILogger<OwnPoseStore> _logger;
    private readonly SimulatorMetrics _metrics;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly SimulationPause _pause;
    private readonly ConcurrentDictionary<string, SourceFix> _sources = new(StringComparer.Ordinal);
    private readonly Lock _writeGate = new();

    private Position? _published;

    /// <summary>Creates the store.</summary>
    public OwnPoseStore(
        PositionEventBroker broker,
        IOptionsMonitor<SimulatorOptions> options,
        SimulatorMetrics metrics,
        SimulationPause pause,
        ILogger<OwnPoseStore> logger)
    {
        _broker = broker;
        _options = options;
        _metrics = metrics;
        _pause = pause;
        _logger = logger;
    }

    /// <summary>Number of position sources that have reported at least once.</summary>
    public int SourceCount => _sources.Count;

    /// <summary>One source's latest fix and when this simulator received it.</summary>
    private sealed record SourceFix(Point? PointLocation, DateTimeOffset ReceivedAt, long Sequence);

    /// <summary>
    ///     Applies one position update. Rejected while the simulator is paused (see
    ///     <see cref="SimulationPause" />).
    ///     <paramref name="update" /> is nullable because the RPC's own field is
    ///     optional on the wire: a request that simply omits the position is a client
    ///     mistake the contract allows to be expressed, so it is answered with an
    ///     error header rather than crashing the call.
    /// </summary>
    public IngestResult UpdatePosition(UpdatePosition? update, DateTimeOffset now)
    {
        var result = UpdatePositionCore(update, now);
        if (!result.Success) _metrics.RecordRejectedBatch();
        return result;
    }

    private IngestResult UpdatePositionCore(UpdatePosition? update, DateTimeOffset now)
    {
        if (update is null) return IngestResult.Fail("UpdatePosition is missing the required position.");
        if (_pause.IsPaused)
        {
            _logger.PositionRejectedWhilePaused();
            return IngestResult.Fail(SimulationPause.PausedMessage);
        }

        if (string.IsNullOrEmpty(update.SourceIdentifier))
        {
            _logger.PositionMissingSource();
            return IngestResult.Fail("Position update is missing the required source_identifier.");
        }

        Position? toPublish;
        lock (_writeGate)
        {
            // Sequence is the tiebreaker the timestamps can't be: two sources
            // reporting inside the same clock tick must still have a most-recent one.
            var sequence = _sources.Count == 0 ? 1 : _sources.Values.Max(fix => fix.Sequence) + 1;
            _sources[update.SourceIdentifier] = new SourceFix(update.PointLocation?.Clone(), now, sequence);
            toPublish = PublishIfChanged(now);
        }

        _logger.PositionUpdated(update.SourceIdentifier);
        _metrics.RecordPositionUpdated();

        if (toPublish is not null) _broker.Publish([toPublish]);
        return IngestResult.Ok;
    }

    /// <summary>
    ///     The position currently selected as primary, or null if no source has ever
    ///     reported. Freshly evaluated against <paramref name="now" /> so a caller
    ///     never sees a fix reported as valid past its timeout just because nothing
    ///     has swept yet.
    /// </summary>
    public Position? GetPosition(DateTimeOffset now)
    {
        lock (_writeGate)
        {
            var current = Evaluate(now);
            if (current is not null) _published = current;
            return current;
        }
    }

    /// <summary>
    ///     Re-evaluates the primary position and announces it if it changed - which,
    ///     absent any new update, means its validity flipped. Called by
    ///     <see cref="OwnPoseStalenessSweeper" />; returns true if something was
    ///     published.
    /// </summary>
    public bool RefreshValidity(DateTimeOffset now)
    {
        if (_pause.IsPaused) return false;

        Position? toPublish;
        lock (_writeGate)
        {
            toPublish = PublishIfChanged(now);
        }

        if (toPublish is null) return false;

        _logger.PositionExpired(toPublish.SourceIdentifier ?? "(unset)");
        _broker.Publish([toPublish]);
        return true;
    }

    /// <summary>Drops every source's fix, for the Host's control endpoints.</summary>
    public int Clear()
    {
        lock (_writeGate)
        {
            var count = _sources.Count;
            _sources.Clear();
            _published = null;
            if (count > 0) _logger.OwnPoseCleared(count);
            return count;
        }
    }

    /// <summary>
    ///     Computes the primary position and, if it differs from the last one
    ///     announced, records it as announced and returns it. Callers hold
    ///     <see cref="_writeGate" />; the actual fan-out happens outside the lock.
    /// </summary>
    private Position? PublishIfChanged(DateTimeOffset now)
    {
        var current = Evaluate(now);
        if (current is null || current.Equals(_published)) return null;

        _published = current;
        return current;
    }

    private Position? Evaluate(DateTimeOffset now)
    {
        if (_sources.IsEmpty) return null;

        var configured = _options.CurrentValue.OwnPose.PrimarySource;
        KeyValuePair<string, SourceFix> primary;

        if (!string.IsNullOrEmpty(configured))
        {
            // A configured primary that has never reported is not silently replaced
            // by another source: the application picked that sensor, and answering
            // with a different one would hide exactly the misconfiguration a client
            // is likeliest to hit.
            if (!_sources.TryGetValue(configured, out var fix)) return null;
            primary = new KeyValuePair<string, SourceFix>(configured, fix);
        }
        else
        {
            primary = _sources.MaxBy(entry => entry.Value.Sequence);
        }

        var timeout = _options.CurrentValue.OwnPose.PositionTimeout;
        var expired = now - primary.Value.ReceivedAt >= timeout;

        return new Position
        {
            SourceIdentifier = primary.Key,
            PointLocation = primary.Value.PointLocation?.Clone(),

            // Two ways to be invalid, and the contract names both: no coordinates at
            // all ("Might be null if unset or invalid"), or coordinates that have
            // stopped being refreshed.
            IsInvalidOrExpired = expired || primary.Value.PointLocation is null
        };
    }
}
