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
///     wreckage behind. Each declares whether it writes at all, so the read-only
///     subset can be pointed at a situation somebody is relying on.
/// </summary>
public static class SituationContractChecks
{
    private static readonly TimeSpan ExpirySweepAllowance = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long the extension check waits before deciding an extended object
    ///     survived. It has to exceed the implementation's sweep interval, which is not
    ///     something the contract specifies or this tool can discover - so this is a
    ///     judgement call, generous enough to cover a typical periodic sweep. Too short
    ///     and the check passes because nothing has swept yet, which is worse than
    ///     taking ten seconds.
    /// </summary>
    private static readonly TimeSpan ExpiryExtensionSettleTime = TimeSpan.FromSeconds(10);

    /// <summary>Every check, in the order they are run and reported.</summary>
    public static IReadOnlyList<ConformanceCheck> All { get; } = [.. BuildAll()];

    private static IEnumerable<ConformanceCheck> BuildAll()
    {
        // --- Read-only probes: safe against a situation somebody is relying on -----
        yield return new ConformanceCheck("get-reachable",
            "GetSituationObjects answers with a successful header",
            "Returns all non-deleted objects in the tactical situation.",
            CheckSeverity.Required, false, false, GetReachableAsync);

        yield return new ConformanceCheck("snapshot-well-formed",
            "Every object in the snapshot has a type, an identity, and is not flagged deleted",
            "Returns all non-deleted objects in the tactical situation.",
            CheckSeverity.Required, false, false, SnapshotWellFormedAsync);

        yield return new ConformanceCheck("subscribe-opens",
            "SubscribeSituationObjectEvents accepts a subscription and streams without error",
            "Can be used to get new situation objects on the map when available.",
            CheckSeverity.Required, false, false, SubscribeOpensAsync);

        // --- Write/merge semantics -------------------------------------------------
        yield return new ConformanceCheck("add-get-roundtrip",
            "An added object is visible in the next snapshot",
            "Creates a new situation object (if none exists for the given ID) or updates an existing one.",
            CheckSeverity.Required, true, false, AddGetRoundTripAsync);

        yield return new ConformanceCheck("partial-update-preserves-omitted",
            "An omitted UpdateProperty leaves the stored value untouched",
            "These data properties specify the fields to be changed. If the value should not be changed, "
            + "then omit the entire property.",
            CheckSeverity.Required, true, false, PartialUpdatePreservesOmittedAsync);

        yield return new ConformanceCheck("partial-update-replaces-present",
            "A present UpdateProperty replaces the stored value",
            "Only non-null fields are updated.",
            CheckSeverity.Required, true, false, PartialUpdateReplacesPresentAsync);

        yield return new ConformanceCheck("null-content-clears",
            "A present UpdateProperty with no content clears the stored value",
            "That means the content value can be null.",
            CheckSeverity.Required, true, false, NullContentClearsAsync);

        yield return new ConformanceCheck("last-write-wins",
            "An update older than the stored reporting_time is ignored",
            "Only the most up-to-date information is considered.",
            CheckSeverity.Required, true, false, LastWriteWinsAsync);

        yield return new ConformanceCheck("creation-metadata-stamped",
            "A written property carries the update's own reporter and reporting_time as its creation_meta_data",
            "This meta data ... describes the timestamp and the reporter of object/property creation or last update.",
            CheckSeverity.Required, true, false, CreationMetaDataStampedAsync);

        yield return new ConformanceCheck("delete-hides-from-snapshot",
            "A deleted object disappears from GetSituationObjects",
            "Returns all non-deleted objects in the tactical situation.",
            CheckSeverity.Required, true, false, DeleteHidesFromSnapshotAsync);

        yield return new ConformanceCheck("missing-identity-rejected",
            "An update without an identity is rejected with an error header",
            "Required: The unique identity of the symbol.",
            CheckSeverity.Required, true, false, MissingIdentityRejectedAsync);

        yield return new ConformanceCheck("missing-reporting-time-rejected",
            "An update without a reporting_time is rejected with an error header",
            "Required: Time of changes.",
            CheckSeverity.Required, true, false, MissingReportingTimeRejectedAsync);

        yield return new ConformanceCheck("delete-missing-identity-rejected",
            "A delete without an identity is rejected with an error header",
            "Required: The unique identity of the symbol. (DeleteSituationObject)",
            CheckSeverity.Required, true, false, DeleteMissingIdentityRejectedAsync);

        yield return new ConformanceCheck("typeless-update-rejected",
            "An update carrying no object type at all is rejected",
            "Not stated by the contract: the oneof can legally be empty on the wire, and the simulator "
            + "refuses such an update because there is nothing to store. Ignoring it is a defensible reading.",
            CheckSeverity.Advisory, true, false, TypelessUpdateRejectedAsync);

        yield return new ConformanceCheck("error-header-explains",
            "A rejected request explains itself in header.error_message",
            "Not stated by the contract: error_message is a nullable StringValue, so leaving it empty is "
            + "legal. An implementation that does leave it empty gives a client nothing to act on.",
            CheckSeverity.Advisory, true, false, ErrorHeaderExplainsAsync);

        yield return new ConformanceCheck("mixed-type-batch",
            "One batch carrying several different object types is applied in full",
            "Specifies the situation objects to be added or updated - a repeated UpdateSituationObject, "
            + "each with its own type.",
            CheckSeverity.Required, true, false, MixedTypeBatchAsync);

        // --- Streaming -------------------------------------------------------------
        yield return new ConformanceCheck("subscribe-snapshot-first",
            "SubscribeSituationObjectEvents opens with the existing situation",
            "Initially, all non-deleted existing situation objects are returned for every call.",
            CheckSeverity.Required, true, false, SubscribeSnapshotFirstAsync);

        yield return new ConformanceCheck("subscribe-live-events",
            "A change made after subscribing arrives on the stream",
            "Can be used to get new situation objects on the map when available.",
            CheckSeverity.Required, true, false, SubscribeLiveEventsAsync);

        yield return new ConformanceCheck("subscribe-excludes-deleted-from-snapshot",
            "A previously deleted object is absent from a new subscription's initial snapshot",
            "Initially, all NON-DELETED existing situation objects are returned for every call.",
            CheckSeverity.Required, true, false, SubscribeExcludesDeletedAsync);

        yield return new ConformanceCheck("subscribe-fans-out",
            "Two concurrent subscribers both receive the same change",
            "Returned for every call - a subscription is per-client, not a single shared consumer.",
            CheckSeverity.Required, true, false, SubscribeFansOutAsync);

        yield return new ConformanceCheck("subscribe-announces-deletes",
            "A delete is announced on the stream, flagged as deleted",
            "Not stated by the contract: the simulator announces deletes so a subscriber learns of a removal "
            + "without re-polling. An implementation that only reflects deletes in GetSituationObjects is "
            + "a defensible reading.",
            CheckSeverity.Advisory, true, false, SubscribeAnnouncesDeletesAsync);

        // --- Tolerance -------------------------------------------------------------
        yield return new ConformanceCheck("update-after-delete-revives",
            "An update newer than the delete that hid an object brings it back",
            "The contract does not settle what a delete does to a later update of the same identity. The "
            + "reading applied here: is_deleted is a timestamped property like any other, so a newer update "
            + "clears it. The alternative poisons an identity permanently - every later write is acknowledged "
            + "while the object stays invisible - which also loses any track that expires on its own and is "
            + "then reported again.",
            CheckSeverity.Advisory, true, false, UpdateAfterDeleteRevivesAsync);

        yield return new ConformanceCheck("unknown-delete-tolerated",
            "Deleting an identity that doesn't exist is not an error",
            "Not stated by the contract: the simulator treats it as a no-op, on the grounds that a client "
            + "retrying a delete shouldn't be punished for succeeding the first time.",
            CheckSeverity.Advisory, true, false, UnknownDeleteToleratedAsync);

        yield return new ConformanceCheck("empty-batch-accepted",
            "An AddOrUpdateSituationObjects request carrying no objects succeeds",
            "Not stated by the contract: the simulator accepts it as a no-op rather than an error.",
            CheckSeverity.Advisory, true, false, EmptyBatchAcceptedAsync);

        yield return new ConformanceCheck("repeated-update-is-idempotent",
            "Sending the identical update twice leaves the object unchanged",
            "Make sure to use the same timestamp as before when nothing changed.",
            CheckSeverity.Advisory, true, false, RepeatedUpdateIsIdempotentAsync);

        yield return new ConformanceCheck("batch-applies-every-object",
            "A batch of many objects is applied in full",
            "Specifies the situation objects to be added or updated (a repeated field).",
            CheckSeverity.Required, true, false, BatchAppliesEveryObjectAsync);

        // --- Property shapes the contract declares but a naive merge tends to miss ---
        yield return new ConformanceCheck("foreign-key-stored",
            "A foreign_key set by an update appears among the object's foreign_keys",
            "A foreign key for mediation between external systems/interfaces. The update model carries one "
            + "(content + source); the stored model keeps a dictionary of them.",
            CheckSeverity.Required, true, false, ForeignKeyStoredAsync);

        yield return new ConformanceCheck("byte-array-property-roundtrip",
            "A byte-array property round-trips with both its content and its MIME type",
            "Byte array (empty was handled as null). The byte array type as MIME type.",
            CheckSeverity.Required, true, false, ByteArrayPropertyAsync);

        yield return new ConformanceCheck("references-property-replaces",
            "A references property replaces the whole list rather than appending to it",
            "List of identities for reference between situation objects - a present UpdateProperty replaces "
            + "the stored value.",
            CheckSeverity.Required, true, false, ReferencesPropertyAsync);

        yield return new ConformanceCheck("dimension-property-roundtrip",
            "A dimension property round-trips all three of its components",
            "Nullable integer 32 value in [m] - x, y and z, a property with no single 'content' field.",
            CheckSeverity.Required, true, false, DimensionPropertyAsync);

        yield return new ConformanceCheck("overlay-nests-objects",
            "An overlay document stores the situation objects nested inside it",
            "The contained overlay situation objects. List of situation object.",
            CheckSeverity.Required, true, false, OverlayNestsObjectsAsync);

        yield return new ConformanceCheck("stream-headers-successful",
            "Every response on the event stream carries a successful header",
            "Specifies if the command was successful (ResponseHeader, on every streamed response).",
            CheckSeverity.Required, true, false, StreamHeadersSuccessfulAsync);

        // --- Object type coverage --------------------------------------------------
        // One per type, generated from the descriptors. Without these the whole suite
        // could pass against an implementation that only ever handles Symbol, which is
        // the single most likely way for a real integration to come apart - every
        // other check in this file uses Symbol and would be perfectly happy.
        foreach (var type in SituationObjects.AllTypes) yield return ObjectTypeCheck(type);

        // --- Identity kind coverage --------------------------------------------------
        // Identity is a oneof of four, and every other check in this file uses
        // string_identity. An implementation that keys its store on the wrong oneof
        // field - or handles only strings - passes everything else.
        foreach (var kind in SituationObjects.IdentityKinds) yield return IdentityKindCheck(kind);

        // --- Location kind coverage --------------------------------------------------
        // SymbolLocation is a oneof of nine, and every other check uses point. Areas,
        // corridors and routes are the tactically interesting ones and the likeliest to
        // have been left out.
        foreach (var kind in SituationObjects.LocationKinds) yield return LocationKindCheck(kind);

        // --- Slow ------------------------------------------------------------------
        yield return new ConformanceCheck("expiry-extension-prevents-deletion",
            "Pushing expiry_time into the future keeps an object alive",
            "It's possible to extend this time.",
            CheckSeverity.Required, true, true, ExpiryExtensionPreventsDeletionAsync);

        yield return new ConformanceCheck("expiry-marks-deleted",
            "An object whose expiry_time has passed is marked deleted on its own",
            "Expired symbols are automatically marked as deleted and removed from the map. "
            + "(The contract states the behaviour but not a deadline; this allows "
            + $"{ExpirySweepAllowance.TotalSeconds:F0}s.)",
            CheckSeverity.Required, true, true, ExpiryMarksDeletedAsync);
    }

    // --- Rejection and tolerance -------------------------------------------------------

    private static async Task<CheckResult> DeleteMissingIdentityRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var request = new DeleteSituationObjectsRequest();
        request.SituationObjects.Add(new DeleteSituationObject
        {
            Reporter = context.Reporter,
            ReportingTime = ConformanceContext.Now()
        });

        var response = await context.Client
            .DeleteSituationObjectsAsync(request, cancellationToken: token)
            .ConfigureAwait(false);

        return response.Header.Success
            ? CheckResult.Fail("a delete with no identity was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> TypelessUpdateRejectedAsync(
        ConformanceContext context, CancellationToken token)
    {
        // A legal message on the wire - the oneof is simply unset - that names no
        // object to store.
        var header = await context.AddOrUpdateAsync(token, new UpdateSituationObject()).ConfigureAwait(false);

        return header.Success
            ? CheckResult.Fail("an update carrying no object type was accepted")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> ErrorHeaderExplainsAsync(
        ConformanceContext context, CancellationToken token)
    {
        // Provoked with the one rejection every implementation should agree on.
        var update = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol { Reporter = context.Reporter, ReportingTime = ConformanceContext.Now() }
        };

        var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
        if (header.Success) return CheckResult.Skip("the implementation accepted an update with no identity");

        return string.IsNullOrWhiteSpace(header.ErrorMessage)
            ? CheckResult.Fail("the request was rejected with an empty error_message")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> MixedTypeBatchAsync(ConformanceContext context, CancellationToken token)
    {
        var now = ConformanceContext.Now();
        var identities = SituationObjects.AllTypes
            .Select(type => (Type: type, Identity: context.NewIdentity($"mixed-{SituationObjects.SlugOf(type)}")))
            .ToList();

        try
        {
            var updates = identities
                .Select(entry => SituationObjects.CreateMinimalUpdate(
                    entry.Type, entry.Identity, context.Reporter, now))
                .ToArray();

            var header = await context.AddOrUpdateAsync(token, updates).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the mixed batch was rejected: {header.ErrorMessage}");

            var snapshot = await context.GetAllAsync(token).ConfigureAwait(false);
            var missing = identities
                .Where(entry => !snapshot.Any(o => entry.Identity.Equals(SituationObjects.IdentityOf(o))))
                .Select(entry => SituationObjects.SlugOf(entry.Type))
                .ToList();

            return missing.Count == 0
                ? CheckResult.Pass($"{identities.Count} types in one call")
                : CheckResult.Fail($"missing from the snapshot after a mixed batch: {string.Join(", ", missing)}");
        }
        finally
        {
            await context.TryCleanupAsync([.. identities.Select(entry => entry.Identity)]).ConfigureAwait(false);
        }
    }

    // --- Property shapes ---------------------------------------------------------------

    private static async Task<CheckResult> ForeignKeyStoredAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("foreign-key");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.ForeignKey = new UpdatePropertyIdentity
                {
                    Content = new Identity { StringIdentity = "GIS-4471" },
                    Source = "gis"
                })).ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null) return CheckResult.Fail("the object was not in the snapshot after a successful write");

            // Deliberately not asserting the map KEY: the contract says the stored model
            // is a dictionary of foreign keys, but never says the key is the source.
            var match = stored.Symbol.ForeignKeys.Values
                .FirstOrDefault(fk => fk.Content?.StringIdentity == "GIS-4471");

            if (match is null)
                return CheckResult.Fail(
                    $"the foreign key was not stored (foreign_keys holds {stored.Symbol.ForeignKeys.Count} entry/entries)");

            return match.Source == "gis"
                ? CheckResult.Pass()
                : CheckResult.Fail($"the stored foreign key's source was '{match.Source}', expected 'gis'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> ByteArrayPropertyAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("byte-array");
        var payload = Google.Protobuf.ByteString.CopyFrom(1, 2, 3, 4, 5);
        try
        {
            // A picture document is the natural carrier; the property shape (content
            // plus a separate MIME type) is what is under test.
            var update = new UpdateSituationObject
            {
                PictureDocument = new UpdatePictureDocument
                {
                    Identity = identity,
                    Reporter = context.Reporter,
                    ReportingTime = ConformanceContext.Now(),
                    PictureData = new UpdatePropertyByteArray { Content = payload, Type = "image/png" }
                }
            };

            var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

            var stored = await context.FindAnyAsync(identity, token).ConfigureAwait(false);
            if (stored?.PictureDocument?.PictureData is not { } picture)
                return CheckResult.Fail("the byte-array property was not stored");

            if (!picture.Content.Equals(payload))
                return CheckResult.Fail(
                    $"the content came back as {picture.Content.Length} byte(s), expected {payload.Length}");

            return picture.Type == "image/png"
                ? CheckResult.Pass()
                : CheckResult.Fail($"the MIME type came back as '{picture.Type}', expected 'image/png'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> ReferencesPropertyAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("references");
        try
        {
            var first = new Identity { StringIdentity = "ref-1" };
            var second = new Identity { StringIdentity = "ref-2" };

            var initial = new UpdateSituationObject
            {
                ActionTask = new UpdateActionTask
                {
                    Identity = identity,
                    Reporter = context.Reporter,
                    ReportingTime = ConformanceContext.Now(),
                    ActionTaskResources = new UpdatePropertyReferences { Contents = { first, second } }
                }
            };
            await context.AddOrUpdateAsync(token, initial).ConfigureAwait(false);

            // A present property replaces the stored value; a list is no exception.
            var replacement = new UpdateSituationObject
            {
                ActionTask = new UpdateActionTask
                {
                    Identity = identity,
                    Reporter = context.Reporter,
                    ReportingTime = ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                    ActionTaskResources = new UpdatePropertyReferences { Contents = { second } }
                }
            };
            await context.AddOrUpdateAsync(token, replacement).ConfigureAwait(false);

            var stored = await context.FindAnyAsync(identity, token).ConfigureAwait(false);
            var contents = stored?.ActionTask?.ActionTaskResources?.Contents;
            if (contents is null) return CheckResult.Fail("the references property was not stored");

            if (contents.Count != 1)
                return CheckResult.Fail(
                    $"the list holds {contents.Count} identity/identities after being replaced with one - "
                    + "the update appears to have been appended rather than applied");

            return contents[0].Equals(second)
                ? CheckResult.Pass()
                : CheckResult.Fail($"the list holds '{contents[0].StringIdentity}', expected 'ref-2'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> DimensionPropertyAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("dimension");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Dimension = new UpdatePropertyDimension { X = 10, Y = 20, Z = 30 }))
                .ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored?.Symbol?.Dimension is not { } dimension)
                return CheckResult.Fail("the dimension property was not stored");

            // Three components rather than one 'content' - the shape most likely to be
            // half-copied by a merge written against the common case.
            return dimension is { X: 10, Y: 20, Z: 30 }
                ? CheckResult.Pass()
                : CheckResult.Fail($"came back as x={dimension.X}, y={dimension.Y}, z={dimension.Z}, expected 10/20/30");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> OverlayNestsObjectsAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("overlay-nesting");
        var nestedIdentity = context.NewIdentity("overlay-nested-symbol");
        try
        {
            var now = ConformanceContext.Now();
            var update = new UpdateSituationObject
            {
                OverlayDocument = new UpdateOverlayDocument
                {
                    Identity = identity,
                    Reporter = context.Reporter,
                    ReportingTime = now,
                    OverlayData = new UpdatePropertySituationObjects
                    {
                        Contents = { context.SymbolUpdate(nestedIdentity, now,
                            symbol => symbol.Name = new UpdatePropertyString { Content = "NESTED" }) }
                    }
                }
            };

            var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

            var stored = await context.FindAnyAsync(identity, token).ConfigureAwait(false);
            var contents = stored?.OverlayDocument?.OverlayData?.Contents;
            if (contents is null || contents.Count == 0)
                return CheckResult.Fail("the overlay stored no nested objects");

            // The update model nests UpdateSituationObjects; the stored model nests
            // whole SituationObjects, so the implementation has to materialize them.
            var nested = contents[0];
            if (nested.TypeCase != SituationObject.TypeOneofCase.Symbol)
                return CheckResult.Fail($"the nested object came back as {nested.TypeCase}, expected Symbol");

            return nested.Symbol.Name?.Content == "NESTED"
                ? CheckResult.Pass()
                : CheckResult.Fail(
                    $"the nested object's name was '{nested.Symbol.Name?.Content}', expected 'NESTED' - "
                    + "the nested update was stored but not materialized");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> StreamHeadersSuccessfulAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("stream-header");
        try
        {
            var badHeader = await StreamWatcher.FindBadHeaderAsync(
                context,
                () => context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now())),
                token).ConfigureAwait(false);

            return badHeader is null
                ? CheckResult.Pass()
                : CheckResult.Fail($"a streamed response carried an unsuccessful header: {badHeader}");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    // --- Expiry ------------------------------------------------------------------------

    private static async Task<CheckResult> ExpiryExtensionPreventsDeletionAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("expiry-extended");
        try
        {
            // Due to expire almost immediately...
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.ExpiryTime = new UpdatePropertyTimestamp
                {
                    Content = ConformanceContext.Now(TimeSpan.FromSeconds(2))
                })).ConfigureAwait(false);

            // ...then pushed well out, which the contract explicitly permits.
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(
                identity, ConformanceContext.Now(TimeSpan.FromSeconds(1)),
                symbol => symbol.ExpiryTime = new UpdatePropertyTimestamp
                {
                    Content = ConformanceContext.Now(TimeSpan.FromHours(1))
                })).ConfigureAwait(false);

            // Long enough that an implementation sweeping on the original expiry would
            // have acted by now.
            await Task.Delay(ExpiryExtensionSettleTime, token).ConfigureAwait(false);

            return await context.FindAsync(identity, token).ConfigureAwait(false) is not null
                ? CheckResult.Pass()
                : CheckResult.Fail("the object was expired anyway after its expiry_time was extended");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    // --- Identity and location kind coverage -------------------------------------------

    /// <summary>
    ///     Builds the "does this implementation handle this kind of Identity" check.
    ///     Advisory for the same reason the object-type checks are: the contract
    ///     declares four kinds, and marks the two integer ones "not for external use to
    ///     create new objects", so an implementation accepting only some of them is not
    ///     thereby non-conformant. What matters is that you find out before writing a
    ///     client that uses UUIDs against a server that only understands strings.
    /// </summary>
    private static ConformanceCheck IdentityKindCheck(Google.Protobuf.Reflection.FieldDescriptor kind)
    {
        return new ConformanceCheck(
            $"identity-kind-{SituationObjects.SlugOf(kind)}",
            $"An object identified by {kind.Name} can be stored and read back",
            "Identity is a oneof of four kinds. The contract does not require an implementation to accept "
            + "all of them, and marks the integer kinds as not for external use. Reported as a capability.",
            CheckSeverity.Advisory, true, false,
            async (context, token) =>
            {
                var identity = SituationObjects.CreateIdentity(
                    kind, $"conformance-{context.RunId}-{SituationObjects.SlugOf(kind)}");
                try
                {
                    var header = await context
                        .AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now()))
                        .ConfigureAwait(false);
                    if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

                    var stored = await context.FindAnyAsync(identity, token).ConfigureAwait(false);
                    return stored is not null
                        ? CheckResult.Pass()
                        : CheckResult.Fail(
                            "the write was accepted but no object with that identity came back - "
                            + "the identity may have been stored under a different oneof field");
                }
                finally
                {
                    await context.TryCleanupAsync(identity).ConfigureAwait(false);
                }
            });
    }

    /// <summary>
    ///     Builds the "does this implementation handle this kind of location" check.
    ///     Same reasoning as the identity and object-type checks - a capability, not a
    ///     verdict - but the one most worth reading: every other check in this file
    ///     uses a point, and areas, corridors and routes are exactly what a tactical
    ///     client needs and what a thin implementation leaves out.
    /// </summary>
    private static ConformanceCheck LocationKindCheck(Google.Protobuf.Reflection.FieldDescriptor kind)
    {
        return new ConformanceCheck(
            $"location-{SituationObjects.SlugOf(kind)}",
            $"A symbol located by {kind.Name} can be stored and read back",
            "SymbolLocation is a oneof of nine location kinds. The contract does not require an "
            + "implementation to accept all of them. Reported as a capability.",
            CheckSeverity.Advisory, true, false,
            async (context, token) =>
            {
                var identity = context.NewIdentity($"location-{SituationObjects.SlugOf(kind)}");
                try
                {
                    var header = await context.AddOrUpdateAsync(token, context.SymbolUpdate(
                        identity, ConformanceContext.Now(),
                        symbol => symbol.Location = new UpdatePropertyLocation
                        {
                            Content = SituationObjects.CreateLocation(kind)
                        })).ConfigureAwait(false);

                    if (!header.Success) return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

                    var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
                    if (stored?.Symbol?.Location?.Content is not { } location)
                        return CheckResult.Fail("the write was accepted but no location came back");

                    // The oneof case is the whole point: an implementation that stored
                    // the location as some other shape has silently changed the geometry.
                    var actual = SymbolLocation.Descriptor.Oneofs[0].Accessor.GetCaseFieldDescriptor(location);
                    return actual?.Name == kind.Name
                        ? CheckResult.Pass()
                        : CheckResult.Fail(
                            $"the location came back as '{actual?.Name ?? "nothing"}', not '{kind.Name}'");
                }
                finally
                {
                    await context.TryCleanupAsync(identity).ConfigureAwait(false);
                }
            });
    }

    // --- Read-only probes ------------------------------------------------------------

    private static async Task<CheckResult> SnapshotWellFormedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var objects = await context.GetAllAsync(token).ConfigureAwait(false);

        var typeless = 0;
        var identityless = 0;
        var deleted = 0;

        foreach (var obj in objects)
        {
            if (obj.TypeCase == SituationObject.TypeOneofCase.None) typeless++;
            else if (SituationObjects.IdentityOf(obj) is null) identityless++;

            if (obj.IsDeleted?.Content == true) deleted++;
        }

        if (typeless + identityless + deleted == 0)
            return CheckResult.Pass($"{objects.Count} object(s) inspected");

        return CheckResult.Fail(
            $"of {objects.Count} object(s): {typeless} carry no type, {identityless} carry no identity, "
            + $"{deleted} are flagged deleted but were still returned");
    }

    private static async Task<CheckResult> SubscribeOpensAsync(ConformanceContext context, CancellationToken token)
    {
        // Purely read-only: opens a subscription, takes whatever the initial snapshot
        // gives it, and leaves. An empty situation legitimately sends nothing at all,
        // so "no error before the timeout" is the pass condition, not "saw an object".
        var failure = await StreamWatcher
            .TryOpenAsync(context, context.ProbeTimeout, token)
            .ConfigureAwait(false);

        return failure is null
            ? CheckResult.Pass()
            : CheckResult.Fail($"the subscription failed: {failure}");
    }

    // --- Merge semantics ---------------------------------------------------------------

    private static async Task<CheckResult> CreationMetaDataStampedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("metadata");
        var reportingTime = ConformanceContext.Now();
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, reportingTime,
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" })).ConfigureAwait(false);

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null) return CheckResult.Fail("the object was not in the snapshot after a successful write");

            var meta = SituationObjects.MetaDataOf(stored, "name");
            if (meta is null) return CheckResult.Fail("the written property carries no creation_meta_data at all");

            if (!Equals(meta.CreatorIdentity, context.Reporter))
                return CheckResult.Fail(
                    $"creator_identity was '{meta.CreatorIdentity?.StringIdentity}', "
                    + $"expected the update's own reporter '{context.Reporter.StringIdentity}'");

            return Equals(meta.CreationTime, reportingTime)
                ? CheckResult.Pass()
                : CheckResult.Fail(
                    $"creation_time was '{meta.CreationTime}', expected the update's own reporting_time "
                    + $"'{reportingTime}'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> RepeatedUpdateIsIdempotentAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("idempotent");
        var reportingTime = ConformanceContext.Now();
        try
        {
            var update = context.SymbolUpdate(identity, reportingTime,
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" });

            await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
            var first = await context.FindAsync(identity, token).ConfigureAwait(false);

            // The contract asks clients to reuse the timestamp when nothing changed,
            // which only makes sense if resending is genuinely free.
            var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
            var second = await context.FindAsync(identity, token).ConfigureAwait(false);

            if (!header.Success) return CheckResult.Fail($"resending an identical update was rejected: {header.ErrorMessage}");
            if (first is null || second is null) return CheckResult.Fail("the object vanished between the two writes");

            return first.Equals(second)
                ? CheckResult.Pass()
                : CheckResult.Fail("resending an identical update changed the stored object");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> BatchAppliesEveryObjectAsync(
        ConformanceContext context, CancellationToken token)
    {
        const int count = 25;
        var identities = Enumerable.Range(0, count).Select(i => context.NewIdentity($"batch-{i:D2}")).ToArray();
        var now = ConformanceContext.Now();

        try
        {
            var updates = identities
                .Select(id => context.SymbolUpdate(id, now,
                    symbol => symbol.Name = new UpdatePropertyString { Content = "BATCH" }))
                .ToArray();

            var header = await context.AddOrUpdateAsync(token, updates).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the batch was rejected: {header.ErrorMessage}");

            var snapshot = await context.GetAllAsync(token).ConfigureAwait(false);
            var present = snapshot.Count(o => identities.Any(id => id.Equals(SituationObjects.IdentityOf(o))));

            return present == count
                ? CheckResult.Pass($"{count} object(s) in one call")
                : CheckResult.Fail($"only {present} of {count} objects from the batch were stored");
        }
        finally
        {
            await context.TryCleanupAsync(identities).ConfigureAwait(false);
        }
    }

    // --- Tolerance ---------------------------------------------------------------------

    private static async Task<CheckResult> UpdateAfterDeleteRevivesAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("revive");
        try
        {
            await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now(),
                symbol => symbol.Name = new UpdatePropertyString { Content = "ALPHA" })).ConfigureAwait(false);
            await context.DeleteAsync(token, identity).ConfigureAwait(false);

            if (await context.FindAsync(identity, token).ConfigureAwait(false) is not null)
                return CheckResult.Fail("the object was still in the snapshot after being deleted");

            // The same identity, reported again well after the delete - a track that
            // came back, or a source that resumed after its object had expired.
            var header = await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity,
                ConformanceContext.Now(TimeSpan.FromMinutes(1)),
                symbol => symbol.Name = new UpdatePropertyString { Content = "BRAVO" })).ConfigureAwait(false);
            if (!header.Success) return CheckResult.Fail($"the re-report was rejected: {header.ErrorMessage}");

            var stored = await context.FindAsync(identity, token).ConfigureAwait(false);
            if (stored is null)
                return CheckResult.Fail(
                    "the re-report was acknowledged as successful but the object stayed invisible - this "
                    + "identity can never be used again, and a track that expires and comes back is lost");

            return stored.Symbol.Name?.Content == "BRAVO"
                ? CheckResult.Pass()
                : CheckResult.Fail($"the object came back with name '{stored.Symbol.Name?.Content}', expected 'BRAVO'");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    private static async Task<CheckResult> UnknownDeleteToleratedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var header = await context
            .DeleteAsync(token, context.NewIdentity("never-existed"))
            .ConfigureAwait(false);

        return header.Success
            ? CheckResult.Pass()
            : CheckResult.Fail($"deleting an unknown identity was refused: {header.ErrorMessage}");
    }

    private static async Task<CheckResult> EmptyBatchAcceptedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var header = await context.AddOrUpdateAsync(token).ConfigureAwait(false);

        return header.Success
            ? CheckResult.Pass()
            : CheckResult.Fail($"an empty batch was refused: {header.ErrorMessage}");
    }

    // --- Streaming ---------------------------------------------------------------------

    private static async Task<CheckResult> SubscribeExcludesDeletedAsync(
        ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("deleted-snapshot");

        await context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now()))
            .ConfigureAwait(false);
        await context.DeleteAsync(token, identity).ConfigureAwait(false);

        // A subscription opened AFTER the delete must not carry the object at all -
        // "all non-deleted existing situation objects" is explicit about this.
        var seen = await StreamWatcher.WaitForAsync(
            context, obj => Matches(obj, identity), context.ProbeTimeout, null, token).ConfigureAwait(false);

        return seen
            ? CheckResult.Fail("an object deleted before subscribing was still sent in the initial snapshot")
            : CheckResult.Pass();
    }

    private static async Task<CheckResult> SubscribeFansOutAsync(ConformanceContext context, CancellationToken token)
    {
        var identity = context.NewIdentity("fanout");
        try
        {
            // Both subscriptions are opened before the write, and both must see it: an
            // implementation with a single shared consumer would satisfy one of them.
            var first = StreamWatcher.WaitForAsync(
                context, obj => Matches(obj, identity), context.StreamTimeout,
                () => context.AddOrUpdateAsync(token, context.SymbolUpdate(identity, ConformanceContext.Now())),
                token);
            var second = StreamWatcher.WaitForAsync(
                context, obj => Matches(obj, identity), context.StreamTimeout, null, token);

            var results = await Task.WhenAll(first, second).ConfigureAwait(false);

            if (results[0] && results[1]) return CheckResult.Pass();

            return CheckResult.Fail(
                $"only {results.Count(r => r)} of 2 concurrent subscribers received the change");
        }
        finally
        {
            await context.TryCleanupAsync(identity).ConfigureAwait(false);
        }
    }

    // --- Object type coverage ----------------------------------------------------------

    /// <summary>
    ///     Builds the "does this implementation support this object type at all" check
    ///     for one of the eleven types. Generated rather than hand-written so a type
    ///     added upstream is covered automatically, and so no type can be forgotten.
    ///     The object is the minimum the contract permits - identity, reporter,
    ///     reporting_time - because this asks whether the type is supported, not
    ///     whether any particular property of it round-trips.
    ///     Advisory, deliberately. The contract declares eleven types but nowhere says
    ///     an implementation must accept all of them, and a real product supporting a
    ///     subset is not thereby non-conformant. So these report a capability rather
    ///     than deliver a verdict - the summary line spells out which types were
    ///     accepted - and <c>--strict</c> is there for anyone who does need all eleven.
    /// </summary>
    private static ConformanceCheck ObjectTypeCheck(SituationObject.TypeOneofCase type)
    {
        return new ConformanceCheck(
            $"object-type-{SituationObjects.SlugOf(type)}",
            $"An object of type {SituationObjects.NameOf(type)} can be stored and read back",
            "The contract declares this situation object type, but does not state that every implementation "
            + "must accept it. Reported as a capability; use --strict to require all eleven.",
            CheckSeverity.Advisory, true, false,
            async (context, token) =>
            {
                var identity = context.NewIdentity(SituationObjects.SlugOf(type));
                try
                {
                    var update = SituationObjects.CreateMinimalUpdate(
                        type, identity, context.Reporter, ConformanceContext.Now());

                    var header = await context.AddOrUpdateAsync(token, update).ConfigureAwait(false);
                    if (!header.Success)
                        return CheckResult.Fail($"the write was rejected: {header.ErrorMessage}");

                    var snapshot = await context.GetAllAsync(token).ConfigureAwait(false);
                    var stored = snapshot.FirstOrDefault(o => identity.Equals(SituationObjects.IdentityOf(o)));

                    if (stored is null)
                        return CheckResult.Fail("the write was accepted but the object was not in the snapshot");

                    return stored.TypeCase == type
                        ? CheckResult.Pass()
                        : CheckResult.Fail($"the object came back as {stored.TypeCase}, not {type}");
                }
                finally
                {
                    await context.TryCleanupAsync(identity).ConfigureAwait(false);
                }
            });
    }

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
