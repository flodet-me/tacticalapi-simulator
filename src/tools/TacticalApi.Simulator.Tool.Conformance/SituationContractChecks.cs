using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     The contract semantics of <c>rheinmetall.tactical_api.v0.Situation</c>,
///     written as checks that can be run against any implementation of it.
///     This repo has always tested these rules - against its own Host, from inside
///     its own test project. That proves the simulator is right; it can't tell you
///     anything about the implementation you're actually integrating with. These are
///     the same rules aimed outward: point them at an address and find out whether
///     the thing answering there behaves like the contract says.
///     Every check is self-contained (own identities, own cleanup) so the suite can
///     be run repeatedly, in any order, against a live situation without leaving
///     wreckage behind.
/// </summary>
public static class SituationContractChecks
{
    private static readonly TimeSpan ExpirySweepAllowance = TimeSpan.FromSeconds(30);

    /// <summary>Every check, in the order they are run and reported.</summary>
    public static IReadOnlyList<ConformanceCheck> All { get; } =
    [
        new("get-reachable",
            "GetSituationObjects answers with a successful header",
            "Returns all non-deleted objects in the tactical situation.",
            false, GetReachableAsync),
        new("add-get-roundtrip",
            "An added object is visible in the next snapshot",
            "Creates a new situation object (if none exists for the given ID) or updates an existing one.",
            false, AddGetRoundTripAsync),
        new("partial-update-preserves-omitted",
            "An omitted UpdateProperty leaves the stored value untouched",
            "These data properties specify the fields to be changed. If the value should not be changed, "
            + "then omit the entire property.",
            false, PartialUpdatePreservesOmittedAsync),
        new("partial-update-replaces-present",
            "A present UpdateProperty replaces the stored value",
            "Only non-null fields are updated.",
            false, PartialUpdateReplacesPresentAsync),
        new("null-content-clears",
            "A present UpdateProperty with no content clears the stored value",
            "That means the content value can be null.",
            false, NullContentClearsAsync),
        new("last-write-wins",
            "An update older than the stored reporting_time is ignored",
            "Only the most up-to-date information is considered.",
            false, LastWriteWinsAsync),
        new("delete-hides-from-snapshot",
            "A deleted object disappears from GetSituationObjects",
            "Returns all non-deleted objects in the tactical situation.",
            false, DeleteHidesFromSnapshotAsync),
        new("missing-identity-rejected",
            "An update without an identity is rejected with an error header",
            "Required: The unique identity of the symbol.",
            false, MissingIdentityRejectedAsync),
        new("missing-reporting-time-rejected",
            "An update without a reporting_time is rejected with an error header",
            "Required: Time of changes.",
            false, MissingReportingTimeRejectedAsync),
        new("subscribe-snapshot-first",
            "SubscribeSituationObjectEvents opens with the existing situation",
            "Initially, all non-deleted existing situation objects are returned for every call.",
            false, SubscribeSnapshotFirstAsync),
        new("subscribe-live-events",
            "A change made after subscribing arrives on the stream",
            "Can be used to get new situation objects on the map when available.",
            false, SubscribeLiveEventsAsync),
        new("subscribe-announces-deletes",
            "A delete is announced on the stream, flagged as deleted",
            "Marks a situation object as deleted.",
            false, SubscribeAnnouncesDeletesAsync),
        new("expiry-marks-deleted",
            "An object whose expiry_time has passed is marked deleted on its own",
            "Expired symbols are automatically marked as deleted and removed from the map.",
            true, ExpiryMarksDeletedAsync)
    ];

    private static async Task<CheckResult> GetReachableAsync(ConformanceContext context, CancellationToken token)
    {
        var response = await context.Client
            .GetSituationObjectsAsync(new GetSituationObjectsRequest(), cancellationToken: token)
            .ConfigureAwait(false);

        return response.Header.Success
            ? CheckResult.Pass($"{response.SituationObjects.Count} object(s) in the situation")
            : CheckResult.Fail($"header.success was false: {response.Header.ErrorMessage}");
    }

    private static async Task<CheckResult> AddGetRoundTripAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("roundtrip");
        try
        {
            var header = await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" })).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null) return CheckResult.Fail("the object was not in the snapshot after a successful write");

            return stored.Symbol.Name?.Content == "ALPHA"
                ? CheckResult.Pass()
                : CheckResult.Fail($"name was '{stored.Symbol.Name?.Content}', expected 'ALPHA'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> PartialUpdatePreservesOmittedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("preserve");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol =>
                {
                    symbol.Name = new UpdatePropertyString { Content = "ALPHA" };
                    symbol.AdditionalInformation = new UpdatePropertyString { Content = "first" };
                })).ConfigureAwait(false);

            // Second update touches only additional_information; name must survive.
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity,
                ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                symbol => symbol.AdditionalInformation = new UpdatePropertyString { Content = "second" }))
                .ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null) return CheckResult.Fail("the object vanished after a partial update");

            if (stored.Symbol.Name?.Content != "ALPHA")
                return CheckResult.Fail(
                    $"omitted property 'name' became '{stored.Symbol.Name?.Content}', expected it to stay 'ALPHA'");

            return stored.Symbol.AdditionalInformation?.Content == "second"
                ? CheckResult.Pass()
                : CheckResult.Fail(
                    $"updated property was '{stored.Symbol.AdditionalInformation?.Content}', expected 'second'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> PartialUpdateReplacesPresentAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("replace");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" })).ConfigureAwait(false);
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity,
                ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                symbol => symbol.Name = new UpdatePropertyString { Content = "BRAVO" })).ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            return stored?.Symbol.Name?.Content == "BRAVO"
                ? CheckResult.Pass()
                : CheckResult.Fail($"name was '{stored?.Symbol.Name?.Content}', expected 'BRAVO'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> NullContentClearsAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("clear");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" })).ConfigureAwait(false);

            // Property present, content absent: the documented way to clear a value.
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity,
                ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                symbol => symbol.Name = new UpdatePropertyString())).ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null) return CheckResult.Fail("the object vanished after clearing a property");

            return stored.Symbol.Name?.Content is null
                ? CheckResult.Pass()
                : CheckResult.Fail($"name was still '{stored.Symbol.Name?.Content}' after being cleared");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> LastWriteWinsAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("stale");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Name = new UpdatePropertyString { Content = "NEWER" })).ConfigureAwait(false);

            // An hour older than what is already stored: must not take effect.
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity,
                ConformanceContext.Now(TimeSpan.FromHours(-1)),
                symbol => symbol.Name = new UpdatePropertyString { Content = "OLDER" })).ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            return stored?.Symbol.Name?.Content == "NEWER"
                ? CheckResult.Pass()
                : CheckResult.Fail(
                    $"a stale update was applied: name is '{stored?.Symbol.Name?.Content}', expected 'NEWER'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> DeleteHidesFromSnapshotAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("delete");
        await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now()))
            .ConfigureAwait(false);

        var header = await context.DeleteAsync(token, identity).ConfigureAwait(false);
        if (!header.Success) return CheckResult.Fail($"the delete was rejected: {header.ErrorMessage}");

        var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
        return stored is null
            ? CheckResult.Pass()
            : CheckResult.Fail("a deleted object was still returned by GetSituationObjects");
    }

    private static async Task<CheckResult> MissingIdentityRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var update = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol { Reporter = context.Reporter, ReportingTime = ConformanceContext.Now() }
        };

        var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
        return header.Success
            ? CheckResult.Fail("an update with no identity was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> MissingReportingTimeRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var update = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = context.NewIdentity("no-time"),
                Reporter = context.Reporter
            }
        };

        var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
        return header.Success
            ? CheckResult.Fail("an update with no reporting_time was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> SubscribeSnapshotFirstAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("snapshot");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now()))
                .ConfigureAwait(false);

            var seen = await StreamWatcher
                .WaitForAsync(context, obj => Matches(obj, identity), context.StreamTimeout, null, token)
                .ConfigureAwait(false);

            return seen
                ? CheckResult.Pass()
                : CheckResult.Fail(
                    "an object that existed before subscribing never arrived in the initial snapshot");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> SubscribeLiveEventsAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("live");
        try
        {
            // Written only after the stream is open, so it can only arrive as a live event.
            var seen = await StreamWatcher.WaitForAsync(
                context,
                obj => Matches(obj, identity),
                context.StreamTimeout,
                () => context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now())),
                token).ConfigureAwait(false);

            return seen
                ? CheckResult.Pass()
                : CheckResult.Fail("a change made after subscribing never arrived on the stream");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> SubscribeAnnouncesDeletesAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("delete-event");
        await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now()))
            .ConfigureAwait(false);

        var seen = await StreamWatcher.WaitForAsync(
            context,
            obj => Matches(obj, identity) && obj.IsDeleted?.Content == true,
            context.StreamTimeout,
            () => context.DeleteAsync(token, identity),
            token).ConfigureAwait(false);

        return seen
            ? CheckResult.Pass()
            : CheckResult.Fail("a delete was never announced on the stream with is_deleted set");
    }

    private static async Task<CheckResult> ExpiryMarksDeletedAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("expiry");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.ExpiryTime = new UpdatePropertyTimestamp
                {
                    Content = ConformanceContext.Now(TimeSpan.FromSeconds(-1))
                })).ConfigureAwait(false);

            // Sweeping is periodic in any sane implementation, so this polls rather
            // than assuming it happens synchronously with the write.
            var deadline = DateTimeOffset.UtcNow + ExpirySweepAllowance;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await context.FindAsync(identity, token).ConfigureAwait(false) is null) return CheckResult.Pass();
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }

            return CheckResult.Fail(
                $"an object whose expiry_time had passed was still in the snapshot after {ExpirySweepAllowance}");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static bool Matches(SituationObject obj, Identity identity)
    {
        return obj.Symbol?.Identity is { } found && found.Equals(identity);
    }
}
