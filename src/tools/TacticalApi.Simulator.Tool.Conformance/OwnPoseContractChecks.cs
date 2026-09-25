using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     The contract semantics of <c>rheinmetall.tactical_api.v0.OwnPose</c>, written
///     as checks that can be run against any implementation of it.
///     Most of these are advisory, and the reason is in the contract itself: the
///     position handed back is "the one selected as primary position source by the
///     application". An implementation is therefore entitled to accept this tool's
///     update and keep answering with a different sensor's fix, and calling that
///     non-conformant would be this tool substituting its own policy for the
///     application's. What stays <see cref="CheckSeverity.Required" /> is the part
///     the contract does state outright: the calls work, the header says so, and a
///     source identifier is mandatory.
///     The same reasoning decides what counts as a failure here. These checks have to
///     work against an implementation whose own position sources are reporting while
///     the suite runs - a live system, in other words, which is the whole point of
///     pointing it at one. So "some other source is primary" is reported as
///     inconclusive rather than as a failure, and what stays a failure is only a
///     genuine self-contradiction: an implementation that says it has a current
///     position and then opens a subscription without sending one.
///     Like the blue force checks, these cannot clean up: there is no RPC to
///     un-report a position - see <see cref="ConformanceContext" />.
/// </summary>
public static class OwnPoseContractChecks
{
    private const double TestLatitude = 48.137;
    private const double TestLongitude = 11.575;

    /// <summary>Every own-pose check, in the order they are run and reported.</summary>
    public static IReadOnlyList<ConformanceCheck> All { get; } = [.. BuildAll()];

    private static IEnumerable<ConformanceCheck> BuildAll()
    {
        yield return new ConformanceCheck("own-pose-get-reachable",
            "GetPosition answers with a successful header",
            "Can be used to get the current position information.",
            CheckSeverity.Required, false, false, GetReachableAsync);

        yield return new ConformanceCheck("own-pose-subscribe-opens",
            "SubscribePositionChangedEvents accepts a subscription and streams without error",
            "Can be used to get new position information when available.",
            CheckSeverity.Required, false, false, SubscribeOpensAsync);

        yield return new ConformanceCheck("own-pose-position-well-formed",
            "A returned position that carries coordinates is not also flagged invalid, and vice versa",
            "The current position. Might be null if unset or invalid. ... A position might be invalid if it "
            + "has been previously determined via GNSS but no updates have been received recently.",
            CheckSeverity.Advisory, false, false, PositionWellFormedAsync);

        yield return new ConformanceCheck("own-pose-update-accepted",
            "UpdatePosition accepts a fix from a named source",
            "Can be used to update the position of a given position source.",
            CheckSeverity.Required, true, false, UpdateAcceptedAsync);

        yield return new ConformanceCheck("own-pose-missing-source-rejected",
            "An update with no source_identifier is refused",
            "Mandatory: A Identifier of the sensor creating the position.",
            CheckSeverity.Required, true, false, MissingSourceRejectedAsync);

        yield return new ConformanceCheck("own-pose-update-becomes-primary",
            "After an update, GetPosition returns that source's fix",
            "The returned position is the one selected as primary position source by the application - so an "
            + "implementation may legitimately keep answering with a different sensor. Reported, not required.",
            CheckSeverity.Advisory, true, false, UpdateBecomesPrimaryAsync);

        yield return new ConformanceCheck("own-pose-subscribe-initial-position",
            "A subscription is sent the current position before anything changes",
            "Initially, the current position is returned.",
            CheckSeverity.Advisory, true, false, SubscribeInitialPositionAsync);

        yield return new ConformanceCheck("own-pose-subscribe-live-events",
            "A position reported after subscribing arrives on the open stream",
            "Can be used to get new position information when available.",
            CheckSeverity.Advisory, true, false, SubscribeLiveEventsAsync);
    }

    private static async Task<CheckResult> GetReachableAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = await context.GetPositionAsync(token).ConfigureAwait(false);

        if (!response.Header.Success)
            return CheckResult.Fail($"header.success was false: {response.Header.ErrorMessage}");

        // No position at all is a perfectly good answer before any source has
        // reported, and saying so is more useful than asserting one exists.
        return CheckResult.Pass(response.Position is null
            ? "no position selected yet"
            : $"primary source '{response.Position.SourceIdentifier}'");
    }

    private static async Task<CheckResult> SubscribeOpensAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var failure = await BlueForceStreamWatcher
            .TryOpenPositionsAsync(context, context.ProbeTimeout, token)
            .ConfigureAwait(false);

        return failure is null
            ? CheckResult.Pass()
            : CheckResult.Fail($"the subscription failed: {failure}");
    }

    private static async Task<CheckResult> PositionWellFormedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = await context.GetPositionAsync(token).ConfigureAwait(false);
        if (response.Position is not { } position) return CheckResult.Pass("no position selected yet");

        if (position.PointLocation?.GeoPoint is null && !position.IsInvalidOrExpired)
            return CheckResult.Fail(
                "the position carries no coordinates but is not flagged is_invalid_or_expired, so a client "
                + "cannot tell an unset position from a valid one at 0/0");

        return CheckResult.Pass(position.IsInvalidOrExpired ? "flagged invalid/expired" : "valid");
    }

    private static async Task<CheckResult> UpdateAcceptedAsync(ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = await context.UpdatePositionAsync(token, TestLatitude, TestLongitude).ConfigureAwait(false);
        return header.Success
            ? CheckResult.Pass($"reported as source '{context.PositionSource}'")
            : CheckResult.Fail($"the update was rejected: {header.ErrorMessage}");
    }

    private static async Task<CheckResult> MissingSourceRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = await context.OwnPoseClient.UpdatePositionAsync(new UpdatePositionRequest
        {
            Position = new UpdatePosition
            {
                PointLocation = new Point
                {
                    LocationTime = ConformanceContext.Now(),
                    GeoPoint = new GeoPoint
                    {
                        LatitudeCoordinate = TestLatitude,
                        LongitudeCoordinate = TestLongitude
                    }
                }
            }
        }, cancellationToken: token).ConfigureAwait(false);

        return response.Header.Success
            ? CheckResult.Fail("a position update with no source_identifier was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> UpdateBecomesPrimaryAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = await context.UpdatePositionAsync(token, TestLatitude, TestLongitude).ConfigureAwait(false);
        if (!header.Success) return CheckResult.Fail($"the update was rejected: {header.ErrorMessage}");

        var response = await context.GetPositionAsync(token).ConfigureAwait(false);
        if (response.Position is not { } position)
            return CheckResult.Fail("GetPosition returned no position at all after a successful update");

        if (position.SourceIdentifier != context.PositionSource)
            // Inconclusive, not wrong. The contract hands back "the one selected as
            // primary position source by the application", so an implementation with
            // another source of its own reporting alongside this run is entitled to
            // keep answering with it - which is exactly what happens against a live
            // system rather than an idle one.
            return CheckResult.Skip(
                $"'{position.SourceIdentifier}' is the primary source, not the '{context.PositionSource}' just "
                + "reported - allowed, but it means this implementation selects a primary some other way");

        var point = position.PointLocation?.GeoPoint;
        if (point is null) return CheckResult.Fail("the returned position carries no coordinates");

        return Math.Abs(point.LatitudeCoordinate - TestLatitude) < 1e-9 &&
               Math.Abs(point.LongitudeCoordinate - TestLongitude) < 1e-9
            ? CheckResult.Pass()
            : CheckResult.Fail(
                $"the returned position was {point.LatitudeCoordinate}/{point.LongitudeCoordinate}, "
                + $"expected {TestLatitude}/{TestLongitude}");
    }

    private static async Task<CheckResult> SubscribeInitialPositionAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Reported before subscribing, so anything the stream sends is by definition
        // the initial position rather than a live change.
        var header = await context.UpdatePositionAsync(token, TestLatitude, TestLongitude).ConfigureAwait(false);
        if (!header.Success) return CheckResult.Fail($"the update was rejected: {header.ErrorMessage}");

        // What the contract promises is "Initially, the current position is returned" -
        // whatever that position is. Demanding to see *this run's* source instead would
        // make the check fail against any implementation whose own sensors are still
        // reporting, which is every live one, and would point the blame at the server
        // for doing exactly what it is entitled to do.
        var current = await context.GetPositionAsync(token).ConfigureAwait(false);
        if (current.Position is null)
            return CheckResult.Skip(
                "the implementation reports no current position at all, so there is nothing a new subscription "
                + "could open with");

        string? arrived = null;
        var seen = await BlueForceStreamWatcher.WaitForPositionAsync(
            context,
            position =>
            {
                arrived = position.SourceIdentifier;
                return true;
            },
            context.StreamTimeout, null, token).ConfigureAwait(false);

        if (!seen)
            return CheckResult.Fail(
                $"GetPosition reports a current position (source '{current.Position.SourceIdentifier}') but a new "
                + "subscription was sent none at all");

        return CheckResult.Pass(arrived == context.PositionSource
            ? null
            : $"opened with source '{arrived}', this implementation's own primary");
    }

    private static async Task<CheckResult> SubscribeLiveEventsAsync(
        ConformanceContext context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A different coordinate from every other check here, so "arrived on the
        // stream" cannot be satisfied by the initial position of an earlier one.
        const double latitude = TestLatitude + 0.05;

        var seen = await BlueForceStreamWatcher.WaitForPositionAsync(
            context,
            position => position.SourceIdentifier == context.PositionSource
                        && position.PointLocation?.GeoPoint is { } point
                        && Math.Abs(point.LatitudeCoordinate - latitude) < 1e-9,
            context.StreamTimeout,
            () => context.UpdatePositionAsync(token, latitude, TestLongitude),
            token).ConfigureAwait(false);

        if (seen) return CheckResult.Pass();

        // Only the primary position is ever streamed, so a change to a source that
        // isn't primary is not required to appear at all. Against an implementation
        // whose own sensors are reporting alongside this run, that is the normal
        // outcome and says nothing about its streaming.
        var after = await context.GetPositionAsync(token).ConfigureAwait(false);
        if (after.Position?.SourceIdentifier != context.PositionSource)
            return CheckResult.Skip(
                $"'{after.Position?.SourceIdentifier}' is the primary source, not this run's, so a change to "
                + "this run's position is not required to be streamed");

        return CheckResult.Fail(
            "this run's source is the primary one, but a position reported while subscribed never arrived "
            + "on the stream");
    }
}
