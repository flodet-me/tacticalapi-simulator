using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     The contract semantics of <c>rheinmetall.tactical_api.v0.BlueForceTracking</c>,
///     written as checks that can be run against any implementation of it.
///     The rule worth going out of your way for is the one that makes this service
///     different from <c>Situation</c>: "In contrast to the UpdateSituationObject
///     message all fields must be filled in every call". An implementation that
///     quietly merges blue force updates the way it merges situation objects will
///     pass every other check here and then, in the field, keep a callsign or a mount
///     host alive long after the sender stopped reporting it.
///     Unlike the situation checks, these cannot clean up after themselves: the
///     contract gives this service no delete RPC at all, only implicit deletion once
///     a keep-alive timeout elapses. What they write therefore ages out on the
///     implementation's schedule - see <see cref="ConformanceContext" />.
/// </summary>
public static class BlueForceContractChecks
{
    /// <summary>
    ///     How long the timeout check waits for an abandoned blue force to disappear.
    ///     A judgement call, like the situation suite's expiry allowance: the contract
    ///     fixes the client's cadence ("at least every 30s") but leaves the timeout
    ///     itself to the application, so there is no value this tool can know is
    ///     right. Long enough to cover a timeout at the low end of sensible; an
    ///     implementation with a longer one is reported as inconclusive, not wrong.
    /// </summary>
    private static readonly TimeSpan TimeoutAllowance = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan TimeoutPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Every blue force check, in the order they are run and reported.</summary>
    public static IReadOnlyList<ConformanceCheck> All { get; } = [.. BuildAll()];

    private static IEnumerable<ConformanceCheck> BuildAll()
    {
        // --- Read-only probes ------------------------------------------------------
        yield return new ConformanceCheck("blue-force-get-reachable",
            "GetBlueForces answers with a successful header",
            "Returns all the blue forces currently available.",
            CheckSeverity.Required, false, false, GetReachableAsync);

        yield return new ConformanceCheck("blue-force-snapshot-well-formed",
            "Every blue force in the snapshot has an identity and a last_contact_time, and is not flagged deleted",
            "Uniquely identifies the blue force. ... The Last time the blue force sent a keep-alive. "
            + "... true if the blue force is not present anymore.",
            CheckSeverity.Required, false, false, SnapshotWellFormedAsync);

        yield return new ConformanceCheck("blue-force-subscribe-opens",
            "SubscribeBlueForceEvents accepts a subscription and streams without error",
            "Can be used to get updates when blue forces change.",
            CheckSeverity.Required, false, false, SubscribeOpensAsync);

        // --- Write semantics -------------------------------------------------------
        yield return new ConformanceCheck("blue-force-add-get-roundtrip",
            "An added blue force is returned by the next GetBlueForces with its fields intact",
            "Adds or updates a blue force. ... Returns all the blue forces currently available.",
            CheckSeverity.Required, true, false, AddGetRoundTripAsync);

        yield return new ConformanceCheck("blue-force-update-replaces-every-field",
            "A second update that omits a field clears it rather than keeping the previous value",
            "In contrast to the UpdateSituationObject message all fields must be filled in every call "
            + "since blue forces are usually not updated by two systems at the same time.",
            CheckSeverity.Required, true, false, UpdateReplacesEveryFieldAsync);

        yield return new ConformanceCheck("blue-force-missing-identity-rejected",
            "An update with no identity is refused",
            "Mandatory: Uniquely identifies the blue force.",
            CheckSeverity.Required, true, false, MissingIdentityRejectedAsync);

        yield return new ConformanceCheck("blue-force-missing-contact-time-rejected",
            "An update with no last_contact_time is refused",
            "Mandatory: The Last time the blue force sent a keep-alive.",
            CheckSeverity.Advisory, true, false, MissingContactTimeRejectedAsync);

        yield return new ConformanceCheck("blue-force-type-flags-combine",
            "is_vehicle, is_unmanned and is_leader can all be true at once",
            "Describes the type of the blue force. Multiple types are allowed.",
            CheckSeverity.Required, true, false, TypeFlagsCombineAsync);

        yield return new ConformanceCheck("blue-force-mount-host-roundtrip",
            "A blue force's mount_host survives a round trip",
            "The optional unique ID of a mount host. For example, personnel riding on a vehicle or a drone "
            + "mounted on the vehicle.",
            CheckSeverity.Required, true, false, MountHostRoundTripAsync);

        yield return new ConformanceCheck("blue-force-batch-applies-every-force",
            "Every blue force in one call is applied",
            "The blue forces. (repeated UpdateBlueForce blue_forces_to_updates)",
            CheckSeverity.Required, true, false, BatchAppliesEveryForceAsync);

        yield return new ConformanceCheck("blue-force-empty-batch-accepted",
            "A call carrying no blue forces at all is accepted",
            "The contract does not forbid an empty batch; refusing one makes a client special-case "
            + "having nothing to report.",
            CheckSeverity.Advisory, true, false, EmptyBatchAcceptedAsync);

        // --- Streaming -------------------------------------------------------------
        yield return new ConformanceCheck("blue-force-subscribe-snapshot-first",
            "A blue force that existed before subscribing arrives in the initial snapshot",
            "Initially, all existing blue forces are returned for every call.",
            CheckSeverity.Required, true, false, SubscribeSnapshotFirstAsync);

        yield return new ConformanceCheck("blue-force-subscribe-live-events",
            "A blue force added after subscribing arrives on the open stream",
            "Can be used to get updates when blue forces change.",
            CheckSeverity.Required, true, false, SubscribeLiveEventsAsync);

        // --- Implicit deletion (slow) ----------------------------------------------
        yield return new ConformanceCheck("blue-force-keepalive-timeout-deletes",
            "A blue force that stops sending keep-alives is eventually deleted",
            "To make sure a blue force is not deleted this method needs to be called cyclically at least "
            + "every 30s. Deletion is done implicitly when a timeout defined by the application is reached.",
            CheckSeverity.Advisory, true, true, KeepAliveTimeoutDeletesAsync);
    }

    private static async Task<CheckResult> GetReachableAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = await context.BlueForceClient
            .GetBlueForcesAsync(new GetBlueForcesRequest(), cancellationToken: token)
            .ConfigureAwait(false);

        return response.Header.Success
            ? CheckResult.Pass($"{response.BlueForces.Count} blue force(s) currently tracked")
            : CheckResult.Fail($"header.success was false: {response.Header.ErrorMessage}");
    }

    private static async Task<CheckResult> SnapshotWellFormedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var all = await context.GetBlueForcesAsync(token).ConfigureAwait(false);

        foreach (var blueForce in all)
        {
            if (blueForce.Identity is null || blueForce.Identity.TypeCase == Identity.TypeOneofCase.None)
                return CheckResult.Fail("a blue force in the snapshot carries no identity");

            if (blueForce.LastContactTime is null)
                return CheckResult.Fail(
                    $"blue force '{Describe(blueForce)}' carries no last_contact_time");

            if (blueForce.IsDeleted)
                return CheckResult.Fail(
                    $"blue force '{Describe(blueForce)}' is flagged is_deleted but is still 'currently available'");
        }

        return CheckResult.Pass($"{all.Count} blue force(s) checked");
    }

    private static async Task<CheckResult> SubscribeOpensAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var failure = await BlueForceStreamWatcher
            .TryOpenBlueForcesAsync(context, context.ProbeTimeout, token)
            .ConfigureAwait(false);

        return failure is null
            ? CheckResult.Pass()
            : CheckResult.Fail($"the subscription failed: {failure}");
    }

    private static async Task<CheckResult> AddGetRoundTripAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-roundtrip");
        var header = await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(), update =>
            {
                update.Callsign = "ALPHA";
                update.PointLocation = PointAt(52.5, 13.4);
            })).ConfigureAwait(false);

        if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

        var stored = await context.FindBlueForceAsync(identity, token).ConfigureAwait(false);
        if (stored is null) return CheckResult.Fail("the blue force was not returned after a successful write");

        if (stored.Callsign != "ALPHA")
            return CheckResult.Fail($"callsign was '{stored.Callsign}', expected 'ALPHA'");

        var point = stored.PointLocation?.GeoPoint;
        if (point is null) return CheckResult.Fail("point_location did not survive the round trip");

        return Math.Abs(point.LatitudeCoordinate - 52.5) < 1e-9 &&
               Math.Abs(point.LongitudeCoordinate - 13.4) < 1e-9
            ? CheckResult.Pass()
            : CheckResult.Fail(
                $"point_location came back as {point.LatitudeCoordinate}/{point.LongitudeCoordinate}, "
                + "expected 52.5/13.4");
    }

    private static async Task<CheckResult> UpdateReplacesEveryFieldAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-replace");

        await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(), update =>
            {
                update.Callsign = "ALPHA";
                update.MountHost = context.NewIdentity("bf-replace-host");
                update.PointLocation = PointAt(52.5, 13.4);
            })).ConfigureAwait(false);

        // Same blue force, later contact time, callsign and mount host simply absent.
        // Under this service's semantics that means it no longer has either - the
        // situation service's "omit to leave unchanged" rule does not apply here.
        await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                update => update.PointLocation = PointAt(52.6, 13.5))).ConfigureAwait(false);

        var stored = await context.FindBlueForceAsync(identity, token).ConfigureAwait(false);
        if (stored is null) return CheckResult.Fail("the blue force vanished after a second update");

        if (!string.IsNullOrEmpty(stored.Callsign))
            return CheckResult.Fail(
                $"callsign was still '{stored.Callsign}' after an update that omitted it - this service "
                + "replaces every field, it does not merge them");

        if (stored.MountHost is not null && stored.MountHost.TypeCase != Identity.TypeOneofCase.None)
            return CheckResult.Fail(
                "mount_host survived an update that omitted it - this service replaces every field");

        return CheckResult.Pass();
    }

    private static async Task<CheckResult> MissingIdentityRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = await context.AddOrUpdateBlueForcesAsync(token,
            new UpdateBlueForce { LastContactTime = ConformanceContext.Now() }).ConfigureAwait(false);

        return header.Success
            ? CheckResult.Fail("a blue force update with no identity was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> MissingContactTimeRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Advisory: last_contact_time is marked mandatory, but an implementation that
        // stamps its own receipt time instead of refusing the call is being helpful
        // rather than wrong - and the contract never says what to do with a bad one.
        var header = await context.AddOrUpdateBlueForcesAsync(token,
            new UpdateBlueForce { Identity = context.NewIdentity("bf-no-time") }).ConfigureAwait(false);

        return header.Success
            ? CheckResult.Fail("a blue force update with no last_contact_time was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> TypeFlagsCombineAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-types");
        await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(), update =>
            {
                update.Callsign = "MULTI";
                update.BlueForceType = new BlueForceType { IsVehicle = true, IsUnmanned = true, IsLeader = true };
            })).ConfigureAwait(false);

        var stored = await context.FindBlueForceAsync(identity, token).ConfigureAwait(false);
        if (stored is null) return CheckResult.Fail("the blue force was not returned after a successful write");

        var type = stored.BlueForceType;
        if (type is null) return CheckResult.Fail("blue_force_type did not survive the round trip");

        return type.IsVehicle && type.IsUnmanned && type.IsLeader
            ? CheckResult.Pass()
            : CheckResult.Fail(
                $"blue_force_type came back as vehicle={type.IsVehicle}, unmanned={type.IsUnmanned}, "
                + $"leader={type.IsLeader}; all three were set");
    }

    private static async Task<CheckResult> MountHostRoundTripAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var carrier = context.NewIdentity("bf-carrier");
        var rider = context.NewIdentity("bf-rider");
        var now = ConformanceContext.Now();

        var header = await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(carrier, now, update =>
            {
                update.Callsign = "CARRIER";
                update.BlueForceType = new BlueForceType { IsVehicle = true };
            }),
            context.BlueForceUpdate(rider, now, update =>
            {
                update.Callsign = "RIDER";
                update.MountHost = carrier;
            })).ConfigureAwait(false);

        if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

        var stored = await context.FindBlueForceAsync(rider, token).ConfigureAwait(false);
        if (stored is null) return CheckResult.Fail("the mounted blue force was not returned");

        return carrier.Equals(stored.MountHost)
            ? CheckResult.Pass()
            : CheckResult.Fail($"mount_host came back as '{stored.MountHost}', expected '{carrier}'");
    }

    private static async Task<CheckResult> BatchAppliesEveryForceAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        const int count = 10;
        var identities = Enumerable.Range(0, count).Select(i => context.NewIdentity($"bf-batch-{i:D2}")).ToArray();
        var now = ConformanceContext.Now();

        var header = await context.AddOrUpdateBlueForcesAsync(token,
            [.. identities.Select(id => context.BlueForceUpdate(id, now, u => u.Callsign = "BATCH"))])
            .ConfigureAwait(false);
        if (!header.Success) return CheckResult.Fail($"the batch was rejected: {header.ErrorMessage}");

        var all = await context.GetBlueForcesAsync(token).ConfigureAwait(false);
        var present = all.Count(blueForce => identities.Any(id => id.Equals(blueForce.Identity)));

        return present == count
            ? CheckResult.Pass($"{count} blue force(s) in one call")
            : CheckResult.Fail($"only {present} of {count} blue forces from the batch were stored");
    }

    private static async Task<CheckResult> EmptyBatchAcceptedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = await context.AddOrUpdateBlueForcesAsync(token).ConfigureAwait(false);
        return header.Success
            ? CheckResult.Pass()
            : CheckResult.Fail($"an empty batch was refused: {header.ErrorMessage}");
    }

    private static async Task<CheckResult> SubscribeSnapshotFirstAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-snapshot");
        await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(), u => u.Callsign = "SNAPSHOT"))
            .ConfigureAwait(false);

        var seen = await BlueForceStreamWatcher
            .WaitForBlueForceAsync(context, bf => Matches(bf, identity), context.StreamTimeout, null, token)
            .ConfigureAwait(false);

        return seen
            ? CheckResult.Pass()
            : CheckResult.Fail(
                "a blue force that existed before subscribing never arrived in the initial snapshot");
    }

    private static async Task<CheckResult> SubscribeLiveEventsAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-live");

        var seen = await BlueForceStreamWatcher.WaitForBlueForceAsync(
            context,
            bf => Matches(bf, identity),
            context.StreamTimeout,
            () => context.AddOrUpdateBlueForcesAsync(token,
                context.BlueForceUpdate(identity, ConformanceContext.Now(), u => u.Callsign = "LIVE")),
            token).ConfigureAwait(false);

        return seen
            ? CheckResult.Pass()
            : CheckResult.Fail("a blue force added while subscribed never arrived on the stream");
    }

    private static async Task<CheckResult> KeepAliveTimeoutDeletesAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var identity = context.NewIdentity("bf-timeout");
        await context.AddOrUpdateBlueForcesAsync(token,
            context.BlueForceUpdate(identity, ConformanceContext.Now(), u => u.Callsign = "ABANDONED"))
            .ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow + TimeoutAllowance;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeoutPollInterval, token).ConfigureAwait(false);

            if (await context.FindBlueForceAsync(identity, token).ConfigureAwait(false) is null)
                return CheckResult.Pass("the abandoned blue force was implicitly deleted");
        }

        // Not a failure: the timeout is "defined by the application", so an
        // implementation with a longer one is not thereby wrong - and reporting it as
        // wrong would push implementers towards a short timeout for this tool's sake.
        return CheckResult.Skip(
            $"the blue force was still present after {TimeoutAllowance.TotalSeconds:F0}s - inconclusive, "
            + "since the contract leaves the timeout to the application");
    }

    /// <summary>Matches a live (non-deleted) blue force by identity.</summary>
    private static bool Matches(BlueForce blueForce, Identity identity)
    {
        return !blueForce.IsDeleted && identity.Equals(blueForce.Identity);
    }

    private static string Describe(BlueForce blueForce)
    {
        return blueForce.Callsign ?? blueForce.Identity?.ToString() ?? "(unidentified)";
    }

    private static Point PointAt(double latitude, double longitude)
    {
        return new Point
        {
            LocationTime = ConformanceContext.Now(),
            GeoPoint = new GeoPoint { LatitudeCoordinate = latitude, LongitudeCoordinate = longitude }
        };
    }
}
