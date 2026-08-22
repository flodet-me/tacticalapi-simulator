# TacticalApi.Simulator.Tool.Conformance

Point it at a TacticalAPI `Situation` endpoint and find out whether the thing answering there actually behaves the
way the contract says.

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

## It writes to the situation it is checking

There is no way to verify merge semantics without writing something. By default the suite creates objects under a
`conformance:<run-id>:` identity prefix, unique per run, and deletes them again — each check cleans up after
itself, and the prefix is a safety net rather than the plan. It prints this before it starts.

`--read-only` runs only the checks that never write, so it is safe to point at a live situation. You get three
checks instead of thirty-three, but they are real ones: the endpoint answers, its snapshot is well-formed, and it
accepts a subscription.

## What it checks

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
  and asserts there is one generated check per object type in the contract.

A checker that fails its own reference implementation is worthless when pointed at someone else's; one that passes
everything is worse.
