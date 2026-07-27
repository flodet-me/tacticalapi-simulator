using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Identities;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;
using TacticalApi.Simulator.Core.Merging;

namespace TacticalApi.Simulator.Core.Store;

/// <summary>
///     Runtime-only situation state. No persistence by design - restart the host
///     and the situation is empty again.
///     Concurrency model: reads are lock-free against a ConcurrentDictionary;
///     writes are serialized by a single gate and use copy-on-write, so any
///     SituationObject instance handed out is never mutated afterwards and can be
///     streamed to subscribers without cloning (a deliberate performance choice).
///     This is the server-side write path: the gRPC service applies incoming
///     RPCs directly here. Simulation sources never touch this class - they go
///     through <see cref="ISituationIngest" />, a real gRPC client, same as any
///     other external caller.
/// </summary>
public sealed class SituationStore(
    IEnumerable<ISituationObjectMerger> mergers,
    SituationEventBroker broker,
    IOptionsMonitor<SimulatorOptions> options,
    ILogger<SituationStore> logger)
{
    private readonly Dictionary<string, Timestamp> _lastReportingTime = [];
    private readonly FrozenMergerLookup _mergers = new(mergers);
    private readonly ConcurrentDictionary<string, SituationObject> _objects = new();
    private readonly Lock _writeGate = new();

    /// <summary>Number of situation objects currently held (including soft-deleted ones).</summary>
    public int Count => _objects.Count;

    /// <summary>Applies add/update messages. Returns per-batch success.</summary>
    public IngestResult AddOrUpdate(IReadOnlyList<UpdateSituationObject> updates)
    {
        if (updates.Count == 0) return IngestResult.Ok;

        var changed = new List<SituationObject>(updates.Count);
        var maxObjects = options.CurrentValue.Performance.MaxSituationObjects;
        var staleCount = 0;

        lock (_writeGate)
        {
            foreach (var update in updates)
            {
                if (!_mergers.TryGet(update.TypeCase, out var merger))
                {
                    logger.UnsupportedType(update.TypeCase.ToString());
                    return IngestResult.Fail(
                        $"Situation object type '{update.TypeCase}' is not supported by this simulator. " +
                        "Register an ISituationObjectMerger for it to add support.");
                }

                var identity = merger.GetIdentity(update);
                var key = IdentityKey.TryCreate(identity);
                if (key is null)
                {
                    logger.MissingIdentity();
                    return IngestResult.Fail("Update is missing the required identity.");
                }

                var reportingTime = merger.GetReportingTime(update);
                if (reportingTime is null)
                {
                    logger.MissingReportingTime(key);
                    return IngestResult.Fail($"Update '{key}' is missing the required reporting_time.");
                }

                var exists = _objects.TryGetValue(key, out var current);
                if (!exists && _objects.Count >= maxObjects)
                {
                    logger.ObjectLimitReached(maxObjects, key);
                    return IngestResult.Fail(
                        $"Object limit of {maxObjects} reached (Simulator:Performance:MaxSituationObjects).");
                }

                // Last-write-wins per object: stale updates are ignored, not errors.
                if (_lastReportingTime.TryGetValue(key, out var last) &&
                    reportingTime.ToDateTimeOffset() < last.ToDateTimeOffset())
                {
                    logger.UpdateIgnoredStale(key);
                    staleCount++;
                    continue;
                }

                var merged = merger.Merge(current, update);
                _objects[key] = merged;
                _lastReportingTime[key] = reportingTime;
                changed.Add(merged);
            }
        }

        logger.BatchProcessed(updates.Count, changed.Count, staleCount);

        if (changed.Count > 0) broker.Publish(changed);

        return IngestResult.Ok;
    }

    /// <summary>Marks objects as deleted.</summary>
    public IngestResult Delete(IReadOnlyList<DeleteSituationObject> deletes)
    {
        if (deletes.Count == 0) return IngestResult.Ok;

        var changed = new List<SituationObject>(deletes.Count);

        lock (_writeGate)
        {
            foreach (var delete in deletes)
            {
                var key = IdentityKey.TryCreate(delete.Identity);
                if (key is null)
                {
                    logger.MissingIdentity();
                    return IngestResult.Fail("Delete is missing the required identity.");
                }

                if (!_objects.TryGetValue(key, out var current))
                    // Deleting something unknown is a no-op, matching tolerant server behavior.
                    continue;

                if (current.IsDeleted?.Content == true) continue;

                var meta = PropertyMerge.Meta(delete.Reporter, delete.ReportingTime);
                var deleted = current.Clone();
                deleted.IsDeleted = PropertyMerge.Deleted(true, meta);
                _objects[key] = deleted;
                changed.Add(deleted);
            }
        }

        logger.ObjectsDeleted(deletes.Count, changed.Count);

        if (changed.Count > 0) broker.Publish(changed);

        return IngestResult.Ok;
    }

    /// <summary>Snapshot of all non-deleted objects (per GetSituationObjects contract).</summary>
    public IReadOnlyList<SituationObject> GetSnapshot()
    {
        var result = new List<SituationObject>(_objects.Count);
        foreach (var obj in _objects.Values)
            if (obj.IsDeleted?.Content != true)
                result.Add(obj);

        return result;
    }

    /// <summary>
    ///     Marks all objects whose expiry_time has passed as deleted. Called by the
    ///     expiry sweeper background service.
    /// </summary>
    public int SweepExpired(DateTimeOffset now, string reporterId)
    {
        List<DeleteSituationObject>? deletes = null;
        var nowTs = Timestamp.FromDateTimeOffset(now);

        foreach (var (_, obj) in _objects)
        {
            if (obj.IsDeleted?.Content == true) continue;

            var expiry = GetExpiry(obj);
            if (expiry is not null && expiry.ToDateTimeOffset() <= now)
            {
                deletes ??= [];
                deletes.Add(new DeleteSituationObject
                {
                    Identity = GetIdentity(obj),
                    Reporter = new Identity { StringIdentity = reporterId },
                    ReportingTime = nowTs
                });
            }
        }

        if (deletes is null) return 0;

        Delete(deletes);
        return deletes.Count;
    }

    private static Timestamp? GetExpiry(SituationObject obj)
    {
        return obj.TypeCase switch
        {
            SituationObject.TypeOneofCase.Symbol => obj.Symbol.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.ActionTask => obj.ActionTask.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.ActionEvent => obj.ActionEvent.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.OrganizationUnit => obj.OrganizationUnit.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.Route => obj.Route.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.TextDocument => obj.TextDocument.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.PictureDocument => obj.PictureDocument.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.VoiceMessageDocument => obj.VoiceMessageDocument.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.NatoMessageDocument => obj.NatoMessageDocument.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.OverlayDocument => obj.OverlayDocument.ExpiryTime?.Content,
            SituationObject.TypeOneofCase.SketchDocument => obj.SketchDocument.ExpiryTime?.Content,
            _ => null
        };
    }

    private static Identity? GetIdentity(SituationObject obj)
    {
        return obj.TypeCase switch
        {
            SituationObject.TypeOneofCase.Symbol => obj.Symbol.Identity,
            SituationObject.TypeOneofCase.ActionTask => obj.ActionTask.Identity,
            SituationObject.TypeOneofCase.ActionEvent => obj.ActionEvent.Identity,
            SituationObject.TypeOneofCase.OrganizationUnit => obj.OrganizationUnit.Identity,
            SituationObject.TypeOneofCase.Route => obj.Route.Identity,
            SituationObject.TypeOneofCase.TextDocument => obj.TextDocument.Identity,
            SituationObject.TypeOneofCase.PictureDocument => obj.PictureDocument.Identity,
            SituationObject.TypeOneofCase.VoiceMessageDocument => obj.VoiceMessageDocument.Identity,
            SituationObject.TypeOneofCase.NatoMessageDocument => obj.NatoMessageDocument.Identity,
            SituationObject.TypeOneofCase.OverlayDocument => obj.OverlayDocument.Identity,
            SituationObject.TypeOneofCase.SketchDocument => obj.SketchDocument.Identity,
            _ => null
        };
    }

    /// <summary>Array-backed lookup of mergers by oneof case - O(1), allocation-free.</summary>
    private sealed class FrozenMergerLookup
    {
        private readonly ISituationObjectMerger?[] _byCase;

        public FrozenMergerLookup(IEnumerable<ISituationObjectMerger> mergers)
        {
            _byCase = new ISituationObjectMerger?[16];
            foreach (var merger in mergers) _byCase[(int)merger.HandledCase] = merger;
        }

        public bool TryGet(UpdateSituationObject.TypeOneofCase typeCase, out ISituationObjectMerger merger)
        {
            var index = (int)typeCase;
            var found = index >= 0 && index < _byCase.Length ? _byCase[index] : null;
            merger = found!;
            return found is not null;
        }
    }
}
