# TacticalApi.Simulator.Tool.Conformance

Point it at a TacticalAPI endpoint and find out whether the thing answering there actually behaves the way the
contract says — across all three of its services: `Situation`, `BlueForceTracking` and `OwnPose`.

This repository has always tested these rules — against its own Host, from inside its own test project. That proves
the simulator is right; it says nothing about the implementation you are integrating with. This is the same rules
aimed outward.

```bash
# Check the simulator you just started (the default address).
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance

# Check somebody else's implementation, including the slow expiry check.
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- \
  --address http://their-host:5100 --include-slow

# Safe against a situation somebody is relying on: writes nothing at all.
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --read-only

# In a pipeline.
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --junit conformance.xml
```

| Exit code | Meaning |
|-----------|---------|
| `0` | Conformant (no required check failed) |
| `1` | At least one required check failed |
| `2` | Bad arguments |
| `3` | The endpoint could not be reached at all |

`3` exists so a pipeline can tell "your server is down" from "your server is wrong". Treating a dead endpoint as a
conformance failure eventually gets someone to "fix" a perfectly good implementation.

## Required vs advisory

The TacticalAPI contract is a `.proto` file with prose comments, not a specification with normative language. Some
rules are stated outright — *"Required: The unique identity of the symbol"*, *"Returns all non-deleted objects"*.
Others are behaviour this simulator implements and believes correct, but which the contract simply doesn't address.

So each check carries a severity, and the report distinguishes them:

- **required** — the contract states it. A failure prints `FAIL` and fails the run.
- **advisory** — the contract is silent; this is the reading the simulator applies. A failure prints `WARN` and does
  *not* fail the run unless you pass `--strict`.

Telling an implementer their server is non-conformant because it differs from us on something the contract never
mentions would be the fastest way to get this tool ignored. Each advisory check's `requirement` text says plainly
what it is assuming and why the other reading is defensible.

## It writes to the implementation it is checking

There is no way to verify merge semantics without writing something. By default the suite creates objects under a
`conformance:<run-id>:` identity prefix, unique per run, and deletes them again — each check cleans up after
itself, and the prefix is a safety net rather than the plan. It prints this before it starts.

**Blue forces and positions are the exception, and it is the contract's doing.** `BlueForceTracking` has no delete
RPC at all — "deletion is done implicitly when a timeout defined by the application is reached" — and `OwnPose`
has no way to un-report a position. What those checks write therefore cannot be cleaned up by being more careful
about it; it ages out on the implementation's own keep-alive timeout. The tool says so before it starts too.

`--read-only` runs only the checks that never write, so it is safe to point at a live situation. You get a handful
of checks instead of all of them, but they are real ones, and there are now some per service: each endpoint
answers, its snapshot is well-formed, and it accepts a subscription.

## What it checks

### `Situation`

**Read-only** (safe against a live situation)

| Id | Severity | Rule |
|----|----------|------|
| `get-reachable` | required | `GetSituationObjects` answers with a successful header |
| `snapshot-well-formed` | required | Every object in the snapshot has a type and identity and isn't flagged deleted |
| `subscribe-opens` | required | `SubscribeSituationObjectEvents` accepts a subscription and streams without error |

**Merge and write semantics**

| Id | Severity | Rule |
|----|----------|------|
| `add-get-roundtrip` | required | An added object is visible in the next snapshot |
| `partial-update-preserves-omitted` | required | An omitted `UpdateProperty` leaves the stored value untouched |
| `partial-update-replaces-present` | required | A present `UpdateProperty` replaces the stored value |
| `null-content-clears` | required | A present `UpdateProperty` with no content clears the value |
| `last-write-wins` | required | An update older than the stored `reporting_time` is ignored |
| `creation-metadata-stamped` | required | A written property carries the update's own reporter and time as its `creation_meta_data` |
| `delete-hides-from-snapshot` | required | A deleted object disappears from `GetSituationObjects` |
| `missing-identity-rejected` | required | An update with no identity is rejected with an error header |
| `missing-reporting-time-rejected` | required | An update with no `reporting_time` is rejected with an error header |
| `batch-applies-every-object` | required | A batch of many objects is applied in full |

**Streaming**

| Id | Severity | Rule |
|----|----------|------|
| `subscribe-snapshot-first` | required | Subscribing opens with the existing situation |
| `subscribe-live-events` | required | A change made after subscribing arrives on the stream |
| `subscribe-excludes-deleted-from-snapshot` | required | A previously deleted object is absent from a new subscription's snapshot |
| `subscribe-fans-out` | required | Two concurrent subscribers both receive the same change |
| `subscribe-announces-deletes` | advisory | A delete is announced on the stream, flagged as deleted |

**Tolerance** — all advisory; the contract says nothing about any of these.

| Id | Rule |
|----|------|
| `unknown-delete-tolerated` | Deleting an identity that doesn't exist is not an error |
| `empty-batch-accepted` | A request carrying no objects succeeds |
| `repeated-update-is-idempotent` | Sending the identical update twice leaves the object unchanged |
| `update-after-delete-revives` | An update newer than the delete that hid an object brings it back |

`update-after-delete-revives` is advisory because the contract doesn't settle it, but the two readings are not
equally harmless. If a delete is permanent, the identity is poisoned: every later write is acknowledged as
successful while the object stays invisible — and since expired objects are deleted automatically, any track that
outlives its `expiry_time` and is then reported again by the source that still sees it is lost the same way.

**Property shapes** — the ones a merge written against the common case tends to get half-right.

| Id | Severity | Rule |
|----|----------|------|
| `foreign-key-stored` | required | A `foreign_key` set by an update appears among the object's `foreign_keys` |
| `byte-array-property-roundtrip` | required | A byte-array property round-trips content **and** MIME type |
| `references-property-replaces` | required | A references property replaces the whole list rather than appending |
| `dimension-property-roundtrip` | required | A dimension property round-trips all three of x/y/z |
| `overlay-nests-objects` | required | An overlay stores the situation objects nested inside it |
| `stream-headers-successful` | required | Every response on the event stream carries a successful header |

Plain string/int/timestamp properties are already covered by the merge checks above. These five are the shapes
where a naive implementation breaks: two fields instead of one, a list with replace semantics, three components
and no `content` field at all, a single `foreign_key` landing in a `map`, and nested `UpdateSituationObject`s that
have to be materialized into whole `SituationObject`s.

**Rejection and tolerance**

| Id | Severity | Rule |
|----|----------|------|
| `delete-missing-identity-rejected` | required | A delete with no identity is rejected |
| `mixed-type-batch` | required | One batch carrying several different object types is applied in full |
| `unknown-delete-tolerated` | advisory | Deleting an identity that doesn't exist is not an error |
| `empty-batch-accepted` | advisory | A request carrying no objects succeeds |
| `repeated-update-is-idempotent` | advisory | Sending the identical update twice leaves the object unchanged |
| `typeless-update-rejected` | advisory | An update carrying no object type at all is rejected |
| `error-header-explains` | advisory | A rejected request explains itself in `header.error_message` |

### `BlueForceTracking`

| Id | Severity | Rule |
|----|----------|------|
| `blue-force-get-reachable` | required | `GetBlueForces` answers with a successful header |
| `blue-force-snapshot-well-formed` | required | Every blue force has an identity and a `last_contact_time`, and isn't flagged deleted |
| `blue-force-subscribe-opens` | required | `SubscribeBlueForceEvents` accepts a subscription and streams without error |
| `blue-force-add-get-roundtrip` | required | An added blue force comes back with its fields intact |
| `blue-force-update-replaces-every-field` | required | A second update that omits a field **clears** it |
| `blue-force-missing-identity-rejected` | required | An update with no identity is refused |
| `blue-force-missing-contact-time-rejected` | advisory | An update with no `last_contact_time` is refused |
| `blue-force-type-flags-combine` | required | `is_vehicle`, `is_unmanned` and `is_leader` can all be true at once |
| `blue-force-mount-host-roundtrip` | required | `mount_host` survives a round trip |
| `blue-force-batch-applies-every-force` | required | Every blue force in one call is applied |
| `blue-force-empty-batch-accepted` | advisory | A call carrying no blue forces is accepted |
| `blue-force-subscribe-snapshot-first` | required | A pre-existing blue force arrives in the initial snapshot |
| `blue-force-subscribe-live-events` | required | A blue force added while subscribed arrives on the stream |
| `blue-force-keepalive-timeout-deletes` | advisory, slow | An abandoned blue force is eventually deleted |

`blue-force-update-replaces-every-field` is the one to look at first. It is the single rule that makes this service
behave unlike the `Situation` service beside it — *"In contrast to the UpdateSituationObject message all fields
must be filled in every call"* — and an implementation that quietly reuses its situation-object merge passes
everything else here and then, in the field, keeps a callsign or a mount host alive long after its sender stopped
reporting one.

`blue-force-keepalive-timeout-deletes` is advisory and reports **inconclusive** (a skip, not a failure) if the blue
force is still there after 45s. The timeout is "defined by the application", so an implementation with a longer one
is not thereby wrong — and failing it would push implementers towards a short timeout for this tool's sake.

### `OwnPose`

| Id | Severity | Rule |
|----|----------|------|
| `own-pose-get-reachable` | required | `GetPosition` answers with a successful header |
| `own-pose-subscribe-opens` | required | `SubscribePositionChangedEvents` accepts a subscription and streams without error |
| `own-pose-position-well-formed` | advisory | A position with no coordinates is flagged `is_invalid_or_expired` |
| `own-pose-update-accepted` | required | `UpdatePosition` accepts a fix from a named source |
| `own-pose-missing-source-rejected` | required | An update with no `source_identifier` is refused |
| `own-pose-update-becomes-primary` | advisory | After an update, `GetPosition` returns that source's fix |
| `own-pose-subscribe-initial-position` | advisory | A subscription is sent the current position before anything changes |
| `own-pose-subscribe-live-events` | advisory | A position reported after subscribing arrives on the stream |

Most of these are advisory by necessity rather than by caution. The position handed back is "the one selected as
primary position source by the application", so an implementation is entitled to accept this tool's update and keep
answering with a different sensor's fix — calling that non-conformant would be this tool substituting its own
policy for the application's. What stays required is what the contract does state outright: the calls work, the
header says so, and a source identifier is mandatory.

## Capability matrices

Three oneofs in the contract are places an implementation can quietly support a subset — and every hand-written
check in the suite uses `symbol` + `string_identity` + `point`, so without these an implementation handling only
those three would pass everything.

| Family | Cases | Ids |
|--------|-------|-----|
| Object types | 11 | `object-type-symbol`, `object-type-overlay-document`, … |
| Identity kinds | 4 | `identity-kind-uuid-identity`, `identity-kind-string-identity`, … |
| Location kinds | 9 | `location-polygon`, `location-corridor`, `location-route-location`, … |

All generated from the protobuf descriptors, so a case added upstream is covered automatically instead of quietly
going unchecked. All advisory: the contract declares these cases but nowhere says an implementation must accept
all of them — and it explicitly marks the integer identity kinds "not for external use to create new objects". A
product supporting a subset isn't thereby non-conformant. So the report treats them as capabilities and prints the
summary up front:

```
Object types accepted: 8 of 11
  not accepted: overlay-document, sketch-document, voice-message-document

Identity kinds accepted: 2 of 4
  not accepted: int32-identity, int64-identity

Location kinds accepted: 4 of 9
  not accepted: ellipse, fan, sketch-location, corridor, route-location
```

That block is the most useful thing in the report if you are planning an integration. Pass `--strict` if you do
require the full set.

The location checks populate real geometry — three distinct points for a polygon or line, a centre and conjugate
diameter points for an ellipse — because an implementation would be entirely within its rights to reject an empty
polygon, and a check that provokes a defensible rejection reports a false failure. They then assert the location
came back as *the same oneof case*: an implementation that stored your corridor as a line has silently changed the
geometry.

**Slow** (needs `--include-slow`)

| Id | Severity | Rule |
|----|----------|------|
| `expiry-marks-deleted` | required | An object whose `expiry_time` has passed is marked deleted on its own |
| `expiry-extension-prevents-deletion` | required | Pushing `expiry_time` into the future keeps an object alive |

The contract states both outright — *"Expired symbols are automatically marked as deleted"*, *"It's possible to
extend this time"* — but gives no deadline for either, so these allow 30s and 10s respectively. Those numbers are
judgement calls, not contract: too short and the extension check passes merely because nothing has swept yet.

`blue-force-keepalive-timeout-deletes` (above) is the third slow check, and the one place the same judgement call
goes the other way: its allowance is 45s and running past it reports *inconclusive* rather than a failure, because
unlike `expiry_time` the blue force timeout is the application's to choose.

## Options

| Flag | Meaning |
|------|---------|
| `--address <uri>` | Endpoint to check (default `http://localhost:5100`) |
| `--grpc-web` | Use the gRPC-Web transport (HTTP/1.1) instead of native gRPC |
| `--reporter <id>` | Reporter identity to write as (default `TacticalAPI-Conformance`) |
| `--stream-timeout <s>` | Seconds a streaming check waits before failing (default `10`) |
| `--list` | List every check and exit without running anything |
| `--only <ids>` | Run only these checks (comma-separated) |
| `--skip <ids>` | Leave these checks out (comma-separated) |
| `--read-only` | Run only the checks that never write |
| `--include-slow` | Also run checks that take seconds |
| `--json` | Emit the report as JSON instead of text |
| `--junit <path>` | Also write a JUnit XML report to `<path>` |
| `--strict` | Treat advisory failures as failures too |

`--stream-timeout` is worth knowing about: it is also how long each streaming check takes to *fail*, so against a
badly broken implementation the suite spends nearly all its time waiting it out. An id in `--only` or `--skip` that
names no known check is rejected outright — a typo shouldn't look like a pass.

`--junit` writes the format every CI already renders as a test report, so a conformance run shows up as thirty-odd
named assertions rather than a wall of log text nobody reads. Advisory failures are emitted as *skipped* there, so
the CI gate agrees with the exit code.

## Sample output

```
TacticalAPI conformance report for http://their-host:5100/

PASS get-reachable                             GetSituationObjects answers with a successful header (143ms)
     0 object(s) in the situation
FAIL last-write-wins                           An update older than the stored reporting_time is ignored (3ms)
     a stale update was applied: name is 'OLDER', expected 'NEWER'
     contract: Only the most up-to-date information is considered.
WARN unknown-delete-tolerated                  Deleting an identity that doesn't exist is not an error (2ms)
     deleting an unknown identity was refused: no such object
     contract: Not stated by the contract: the simulator treats it as a no-op, on the grounds that a client
     retrying a delete shouldn't be punished for succeeding the first time.

Object types accepted: 11 of 11

31 passed, 2 failed, 0 skipped (1 of the failures advisory - the contract does not settle those)
```

## A note on independence

This project references **only** `TacticalApi.Simulator.Contracts` — the generated contract. Not `Core`, not
`Host`. That is deliberate and worth preserving: a checker that shares code with one implementation can't be
trusted to judge the others. `SituationObjects.cs` therefore duplicates descriptor machinery that also exists in
`Core`, and should keep duplicating it.

## Tests

- `ConformanceReportTests` (unit) — report rendering (text, JSON, JUnit, catalog), the severity verdict, check
  selection, argument parsing, and the runner turning a throwing check into a reported failure instead of aborting
  the run.
- `ConformanceSuiteE2ETests` (E2E) — runs the whole suite against this repo's own Host and requires every check to
  pass; runs it against a Host deliberately configured to reject every write and requires a *required* check to
  fail; proves `--read-only` leaves the situation byte-for-byte unchanged; proves a full run leaves nothing behind;
  asserts there is one generated check per object type in the contract; and asserts all three services are actually
  covered by the default run, since a suite that quietly stopped checking two of them would pass more easily.

A checker that fails its own reference implementation is worthless when pointed at someone else's; one that passes
everything is worse.
