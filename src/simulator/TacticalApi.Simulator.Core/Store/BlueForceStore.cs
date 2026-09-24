using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Control;
using TacticalApi.Simulator.Core.Diagnostics;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Identities;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Runtime-only blue force state, behind the <c>BlueForceTracking</c> service.
///     Same concurrency model as <see cref="SituationStore" /> - lock-free reads
///     over a ConcurrentDictionary, writes serialized by one gate and copy-on-write
///     so a published instance is never mutated afterwards - but deliberately NOT
///     the same update semantics.
///     A blue force is replaced wholesale on every write, not merged property by
///     property, because the contract says so outright: "In contrast to the
///     UpdateSituationObject message all fields must be filled in every call since
///     blue forces are usually not updated by two systems at the same time." There
///     is consequently no per-property CreationMetaData and no merger here; an
///     omitted field means the blue force no longer has one, not that the previous
///     value survives.
///     Deletion is implicit only: the contract has no delete RPC, just "deletion is
///     done implicitly when a timeout defined by the application is reached" - see
///     <see cref="BlueForceTimeoutSweeper" /> and <c>Simulator:BlueForce</c>.
/// </summary>
public sealed class BlueForceStore
{
    private readonly ConcurrentDictionary<string, BlueForce> _blueForces = new();
    private readonly BlueForceEventBroker _broker;
    private readonly Dictionary<string, Timestamp> _lastContactTime = [];
    private readonly ILogger<BlueForceStore> _logger;
    private readonly SimulatorMetrics _metrics;
    private readonly IOptionsMonitor<SimulatorOptions> _options;
    private readonly SimulationPause _pause;
    private readonly Lock _writeGate = new();

    /// <summary>Creates the store and publishes its blue-force-count gauge.</summary>
    public BlueForceStore(
        BlueForceEventBroker broker,
        IOptionsMonitor<SimulatorOptions> options,
        SimulatorMetrics metrics,
        SimulationPause pause,
        ILogger<BlueForceStore> logger)
    {
        _broker = broker;
        _options = options;
        _metrics = metrics;
        _pause = pause;
        _logger = logger;
        _metrics.RegisterBlueForceCount(() => Count);
    }

    /// <summary>Number of blue forces currently tracked.</summary>
    public int Count => _blueForces.Count;

    /// <summary>
    ///     Applies add/update messages, replacing each addressed blue force
    ///     entirely. Rejected while the simulator is paused (see
    ///     <see cref="SimulationPause" />).
    /// </summary>
    public IngestResult AddOrUpdate(IReadOnlyList<UpdateBlueForce> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var result = AddOrUpdateCore(updates);
        if (!result.Success) _metrics.RecordRejectedBatch();
        return result;
    }

    private IngestResult AddOrUpdateCore(IReadOnlyList<UpdateBlueForce> updates)
    {
        if (updates.Count == 0) return IngestResult.Ok;
        if (_pause.IsPaused)
        {
            _logger.BlueForceWriteRejectedWhilePaused(updates.Count);
            return IngestResult.Fail(SimulationPause.PausedMessage);
        }

        var settings = _options.CurrentValue.BlueForce;
        var changed = new List<BlueForce>(updates.Count);
        var staleCount = 0;

        lock (_writeGate)
        {
            foreach (var update in updates)
            {
                var key = IdentityKey.TryCreate(update.Identity);
                if (key is null)
                {
                    _logger.MissingIdentity();
                    return IngestResult.Fail("Blue force update is missing the required identity.");
                }

                if (update.LastContactTime is null)
                {
                    _logger.BlueForceMissingContactTime(key);
                    return IngestResult.Fail($"Blue force '{key}' is missing the required last_contact_time.");
                }

                var exists = _blueForces.ContainsKey(key);
                if (!exists && _blueForces.Count >= settings.MaxBlueForces)
                {
                    _logger.BlueForceLimitReached(settings.MaxBlueForces, key);
                    return IngestResult.Fail(
                        $"Blue force limit of {settings.MaxBlueForces} reached (Simulator:BlueForce:MaxBlueForces).");
                }

                // Last-write-wins per blue force, on last_contact_time - the only
                // ordering the update message carries. Stale reports are ignored,
                // not errors, exactly as for situation objects.
                if (_lastContactTime.TryGetValue(key, out var last) &&
                    update.LastContactTime.ToDateTimeOffset() < last.ToDateTimeOffset())
                {
                    _logger.BlueForceIgnoredStale(key);
                    staleCount++;
                    continue;
                }

                var blueForce = Materialize(update, settings.OwnIdentity);
                _blueForces[key] = blueForce;
                _lastContactTime[key] = update.LastContactTime;
                changed.Add(blueForce);
            }
        }

        _logger.BlueForceBatchProcessed(updates.Count, changed.Count, staleCount);
        _metrics.RecordBlueForcesUpdated(changed.Count);

        if (changed.Count > 0) _broker.Publish(changed);

        return IngestResult.Ok;
    }

    /// <summary>
    ///     Builds the stored blue force from one update. Everything the update
    ///     carries is copied (cloned, so the stored instance shares nothing mutable
    ///     with the request); the two fields <c>UpdateBlueForce</c> has no room for -
    ///     <c>own_blue_force</c> and <c>associated_organization_unit_identity</c> -
    ///     are the answering system's to decide, and this simulator decides the
    ///     first from <c>Simulator:BlueForce:OwnIdentity</c> and leaves the second
    ///     unset since nothing in the contract can tell it one.
    /// </summary>
    private static BlueForce Materialize(UpdateBlueForce update, string? ownIdentity)
    {
        return new BlueForce
        {
            Identity = update.Identity?.Clone(),
            LastContactTime = update.LastContactTime,
            Callsign = update.Callsign,
            Symbol = update.Symbol?.Clone(),
            BlueForceType = update.BlueForceType?.Clone(),
            PointLocation = update.PointLocation?.Clone(),
            MountHost = update.MountHost?.Clone(),
            OwnBlueForce = ownIdentity is not null
                           && update.Identity?.TypeCase == Identity.TypeOneofCase.StringIdentity
                           && string.Equals(update.Identity.StringIdentity, ownIdentity, StringComparison.Ordinal),
            IsDeleted = false
        };
    }

    /// <summary>Snapshot of every blue force currently available (per GetBlueForces).</summary>
    public IReadOnlyList<BlueForce> GetSnapshot()
    {
        var result = new List<BlueForce>(_blueForces.Count);
        foreach (var blueForce in _blueForces.Values)
            if (!blueForce.IsDeleted)
                result.Add(blueForce);

        return result;
    }

    /// <summary>
    ///     Implicitly deletes blue forces whose last keep-alive is older than
    ///     <see cref="BlueForceOptions.KeepAliveTimeout" />. Each is announced on the
    ///     event stream once with <c>is_deleted</c> set and then dropped: a tombstone
    ///     kept forever would leak memory across a long run with churn, and a client
    ///     that missed the announcement learns the same thing from the blue force's
    ///     absence in the next snapshot.
    /// </summary>
    public int SweepTimedOut(DateTimeOffset now)
    {
        // A paused situation is frozen, implicit deletion included - otherwise blue
        // forces would keep vanishing underneath whoever paused it to look at them.
        if (_pause.IsPaused) return 0;

        var timeout = _options.CurrentValue.BlueForce.KeepAliveTimeout;
        List<BlueForce>? deleted = null;

        lock (_writeGate)
        {
            foreach (var (key, blueForce) in _blueForces)
            {
                var lastContact = blueForce.LastContactTime;
                if (lastContact is null || now - lastContact.ToDateTimeOffset() < timeout) continue;

                var tombstone = blueForce.Clone();
                tombstone.IsDeleted = true;

                _blueForces.TryRemove(key, out _);
                _lastContactTime.Remove(key);

                deleted ??= [];
                deleted.Add(tombstone);
            }
        }

        if (deleted is null) return 0;

        _logger.BlueForcesTimedOut(deleted.Count);
        _metrics.RecordBlueForcesExpired(deleted.Count);
        _broker.Publish(deleted);
        return deleted.Count;
    }

    /// <summary>
    ///     Drops every blue force without announcing deletions, for the Host's
    ///     control endpoints - same reasoning as <see cref="SituationStore.Clear" />:
    ///     a reset is a restart of the situation, not a bulk delete of it.
    /// </summary>
    public int Clear()
    {
        lock (_writeGate)
        {
            var count = _blueForces.Count;
            _blueForces.Clear();
            _lastContactTime.Clear();
            _logger.BlueForceStoreCleared(count);
            return count;
        }
    }
}
