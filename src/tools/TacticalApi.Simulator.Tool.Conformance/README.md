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

# In a pipeline.
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --json > conformance.json
```

Exit codes: `0` every check that ran passed, `1` at least one failed, `2` bad arguments.

## It writes to the situation it is checking

There is no way to verify merge semantics without writing something. The suite creates objects under a
`conformance:<run-id>:` identity prefix, unique per run, and deletes them again — each check cleans up after itself,
and the prefix is a safety net rather than the plan. It prints this before it starts. Don't point it at a situation
somebody is relying on.

## What it checks

| Id | Rule |
|----|------|
| `get-reachable` | `GetSituationObjects` answers with a successful header |
| `add-get-roundtrip` | An added object is visible in the next snapshot |
| `partial-update-preserves-omitted` | An omitted `UpdateProperty` leaves the stored value untouched |
| `partial-update-replaces-present` | A present `UpdateProperty` replaces the stored value |
| `null-content-clears` | A present `UpdateProperty` with no content clears the value |
| `last-write-wins` | An update older than the stored `reporting_time` is ignored |
| `delete-hides-from-snapshot` | A deleted object disappears from `GetSituationObjects` |
| `missing-identity-rejected` | An update with no identity is rejected with an error header |
| `missing-reporting-time-rejected` | An update with no `reporting_time` is rejected with an error header |
| `subscribe-snapshot-first` | Subscribing opens with the existing situation |
| `subscribe-live-events` | A change made after subscribing arrives on the stream |
| `subscribe-announces-deletes` | A delete is announced on the stream, flagged as deleted |
| `expiry-marks-deleted` (slow) | An object whose `expiry_time` has passed is marked deleted on its own |

Each check quotes the contract rule it enforces, and a failure prints that quote alongside what actually happened —
the audience for this report is someone about to discuss whose side of the contract is wrong.

## Options

| Flag | Meaning |
|------|---------|
| `--address <uri>` | Endpoint to check (default `http://localhost:5100`) |
| `--grpc-web` | Use the gRPC-Web transport (HTTP/1.1) instead of native gRPC |
| `--reporter <id>` | Reporter identity to write as (default `TacticalAPI-Conformance`) |
| `--include-slow` | Also run checks that take seconds (expiry sweeping) |
| `--stream-timeout <s>` | Seconds a streaming check waits before failing (default `10`) |
| `--json` | Emit the report as JSON instead of text |

`--stream-timeout` is worth knowing about: it is also how long each streaming check takes to *fail*, so against a
badly broken implementation the suite spends nearly all its time waiting it out.

## Sample output

```
TacticalAPI conformance report for http://localhost:5100/

PASS get-reachable                     GetSituationObjects answers with a successful header (143ms)
     0 object(s) in the situation
PASS add-get-roundtrip                 An added object is visible in the next snapshot (78ms)
FAIL last-write-wins                   An update older than the stored reporting_time is ignored (3ms)
     a stale update was applied: name is 'OLDER', expected 'NEWER'
     contract: Only the most up-to-date information is considered.

12 passed, 1 failed, 0 skipped
```

## Tests

- `ConformanceReportTests` (unit) — report rendering (text and JSON), argument parsing, and the runner turning a
  throwing check into a reported failure instead of aborting the run.
- `ConformanceSuiteE2ETests` (E2E) — runs the whole suite against this repo's own Host and requires every check to
  pass, **and** runs it against a Host deliberately configured to reject every write and requires it to fail. A
  checker that fails its own reference implementation is worthless when pointed at someone else's; one that passes
  everything is worse.
