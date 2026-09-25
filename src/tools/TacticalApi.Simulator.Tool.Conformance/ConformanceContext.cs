using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Everything a check needs to talk to the implementation under test, plus the
///     small helpers every check would otherwise repeat.
///     Each run uses a unique identity prefix, so a run leaves nothing behind that
///     could collide with a previous one - and so the objects it creates are
///     recognisable as this tool's while it runs. Checks are still expected to clean
///     up after themselves; the prefix is a safety net, not the plan.
/// </summary>
public sealed class ConformanceContext(
    ChannelBase channel, string reporterId, string runId, TimeSpan? streamTimeout = null)
{
    /// <summary>
    ///     How long a streaming check waits for the object it expects before calling
    ///     the implementation wrong. Configurable because it is also how long every
    ///     streaming check takes to fail: against a badly broken implementation the
    ///     suite spends essentially all its time waiting this out.
    /// </summary>
    public TimeSpan StreamTimeout { get; } = streamTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How long a check watches a stream when it expects to see NOTHING - the
    ///     read-only probe, and the "a deleted object must not appear" check.
    ///     Deliberately a fraction of <see cref="StreamTimeout" />: those checks pass
    ///     by timing out, so the full timeout would be pure waiting, whereas a check
    ///     that waits for something to arrive has to be patient.
    /// </summary>
    public TimeSpan ProbeTimeout => TimeSpan.FromMilliseconds(Math.Max(500, StreamTimeout.TotalMilliseconds / 5));

    /// <summary>The <c>Situation</c> client under test.</summary>
    public Situation.SituationClient Client { get; } = new(channel);

    /// <summary>The <c>BlueForceTracking</c> client under test.</summary>
    public BlueForceTracking.BlueForceTrackingClient BlueForceClient { get; } = new(channel);

    /// <summary>The <c>OwnPose</c> client under test.</summary>
    public OwnPose.OwnPoseClient OwnPoseClient { get; } = new(channel);

    /// <summary>
    ///     Position source identifier this run reports as, so a run cannot be
    ///     confused with the implementation's own sensors - and so two runs against
    ///     the same endpoint don't fight over one source.
    /// </summary>
    public string PositionSource { get; } = $"conformance-{runId}";

    /// <summary>Reporter identity this run stamps on everything it writes.</summary>
    public Identity Reporter { get; } = new() { StringIdentity = reporterId };

    /// <summary>Identity prefix unique to this run.</summary>
    public string RunId { get; } = runId;

    /// <summary>Builds an object identity scoped to this run and check.</summary>
    public Identity NewIdentity(string suffix)
    {
        return new Identity { StringIdentity = $"conformance:{RunId}:{suffix}" };
    }

    /// <summary>Current UTC time as a protobuf timestamp.</summary>
    public static Timestamp Now(TimeSpan offset = default)
    {
        return Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow + offset);
    }

    /// <summary>Builds a minimal Symbol update; property setters are applied by the caller.</summary>
    public UpdateSituationObject SymbolUpdate(Identity identity, Timestamp reportingTime, Action<UpdateSymbol>? configure = null)
    {
        var symbol = new UpdateSymbol
        {
            Identity = identity,
            Reporter = Reporter,
            ReportingTime = reportingTime
        };
        configure?.Invoke(symbol);
        return new UpdateSituationObject { Symbol = symbol };
    }

    /// <summary>Sends one or more updates and returns the response header.</summary>
    public async Task<ResponseHeader> AddOrUpdateAsync(
        CancellationToken cancellationToken, params UpdateSituationObject[] updates)
    {
        var request = new AddOrUpdateSituationObjectsRequest();
        request.SituationObjects.AddRange(updates);
        var response = await Client.AddOrUpdateSituationObjectsAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Header;
    }

    /// <summary>Deletes one or more objects and returns the response header.</summary>
    public async Task<ResponseHeader> DeleteAsync(CancellationToken cancellationToken, params Identity[] identities)
    {
        var request = new DeleteSituationObjectsRequest();
        foreach (var identity in identities)
            request.SituationObjects.Add(new DeleteSituationObject
            {
                Identity = identity,
                Reporter = Reporter,
                ReportingTime = Now()
            });

        var response = await Client.DeleteSituationObjectsAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Header;
    }

    /// <summary>Fetches the full snapshot.</summary>
    public async Task<IReadOnlyList<SituationObject>> GetAllAsync(CancellationToken cancellationToken)
    {
        var response = await Client
            .GetSituationObjectsAsync(new GetSituationObjectsRequest(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.SituationObjects;
    }

    /// <summary>Finds one object in the snapshot by identity, or null if it isn't there.</summary>
    public async Task<SituationObject?> FindAsync(Identity identity, CancellationToken cancellationToken)
    {
        foreach (var obj in await GetAllAsync(cancellationToken).ConfigureAwait(false))
            if (obj.Symbol?.Identity is { } found && found.Equals(identity))
                return obj;

        return null;
    }

    /// <summary>Finds one object in the snapshot by identity, whatever type it carries.</summary>
    public async Task<SituationObject?> FindAnyAsync(Identity identity, CancellationToken cancellationToken)
    {
        var snapshot = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.FirstOrDefault(obj => identity.Equals(SituationObjects.IdentityOf(obj)));
    }

    /// <summary>Builds a minimal blue force update; further fields are set by the caller.</summary>
    public UpdateBlueForce BlueForceUpdate(
        Identity identity, Timestamp lastContactTime, Action<UpdateBlueForce>? configure = null)
    {
        var update = new UpdateBlueForce
        {
            Identity = identity,
            LastContactTime = lastContactTime
        };
        configure?.Invoke(update);
        return update;
    }

    /// <summary>Sends one or more blue force updates and returns the response header.</summary>
    public async Task<ResponseHeader> AddOrUpdateBlueForcesAsync(
        CancellationToken cancellationToken, params UpdateBlueForce[] updates)
    {
        var request = new AddOrUpdateBlueForcesRequest();
        request.BlueForcesToUpdates.AddRange(updates);
        var response = await BlueForceClient
            .AddOrUpdateBlueForcesAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.Header;
    }

    /// <summary>Fetches every blue force the implementation currently has.</summary>
    public async Task<IReadOnlyList<BlueForce>> GetBlueForcesAsync(CancellationToken cancellationToken)
    {
        var response = await BlueForceClient
            .GetBlueForcesAsync(new GetBlueForcesRequest(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return response.BlueForces;
    }

    /// <summary>Finds one blue force by identity, or null if it isn't there.</summary>
    public async Task<BlueForce?> FindBlueForceAsync(Identity identity, CancellationToken cancellationToken)
    {
        var all = await GetBlueForcesAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(blueForce => identity.Equals(blueForce.Identity));
    }

    /// <summary>Reports a position for this run's own source and returns the response header.</summary>
    public async Task<ResponseHeader> UpdatePositionAsync(
        CancellationToken cancellationToken, double latitude, double longitude)
    {
        var response = await OwnPoseClient.UpdatePositionAsync(new UpdatePositionRequest
        {
            Position = new UpdatePosition
            {
                SourceIdentifier = PositionSource,
                PointLocation = new Point
                {
                    LocationTime = Now(),
                    GeoPoint = new GeoPoint { LatitudeCoordinate = latitude, LongitudeCoordinate = longitude }
                }
            }
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Header;
    }

    /// <summary>Fetches the position the implementation currently treats as primary.</summary>
    public async Task<GetPositionResponse> GetPositionAsync(CancellationToken cancellationToken)
    {
        return await OwnPoseClient
            .GetPositionAsync(new GetPositionRequest(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Best-effort cleanup, used in a check's finally block. Swallows failures:
    ///     a cleanup that throws would mask the actual result of the check.
    /// </summary>
    public async Task TryCleanupAsync(params Identity[] identities)
    {
        try
        {
            await DeleteAsync(CancellationToken.None, identities).ConfigureAwait(false);
        }
        catch (Grpc.Core.RpcException)
        {
            // Nothing useful to do: the check's own result is what matters.
        }
    }
}
