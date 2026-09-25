# Testing

Two layers, both run by `dotnet test`.

## Unit tests (`TacticalApi.Simulator.Tests`)

| Area | Covered |
| --- | --- |
| `SituationStore` | merge, last-write-wins, delete, expiry, object cap, reset, pause |
| `BlueForceStore` | whole-object replacement, keep-alive timeout + its one-shot deletion announcement, own-force flag, cap, pause |
| `OwnPoseStore` | primary-source selection, flip to `is_invalid_or_expired` + its announcement, pause |
| Mergers | every one; `AllMergers` meta-test asserts full oneof coverage |
| Rest | event broker + drop accounting, metrics instruments, recording format/writer/reader, stored→update converter, replay player, load generator, track mapping |

Source-specific coverage is documented in each source project's own README.

## E2E tests (`TacticalApi.Simulator.E2ETests`)

Boot the *real* Host via `WebApplicationFactory<Program>` and drive *real* gRPC calls over an in-memory transport. Background sources are off by default in the fixture for determinism; individual tests opt back in via configuration.

| Test class | What it proves |
| --- | --- |
| `SituationService…` | add/get round-trip, partial merge over the wire, delete, error headers, snapshot-then-live events, stale-update rejection, gRPC-Web transport (the official Rheinmetall test client's path), expiry sweeper, HTTP status endpoint |
| `BlueForceTrackingServiceE2ETests` | implicit deletion when keep-alives stop — and *no* deletion while they continue |
| `OwnPoseServiceE2ETests` | a position going `is_invalid_or_expired` while keeping its coordinates, announced to a subscriber sending nothing at all |
| `ControlEndpoints…`, `FaultInjection…`, `MetricsEndpoint…`, `MapApi…` | the Host surfaces beside the contract; the last over `/api/objects`, where both symbol identifier forms must come out as a code the frontend can draw |
| `RecordReplayE2ETests` | records a real Host over a real subscription, resets, replays, checks the situation came back |
| `ConformanceSuiteE2ETests` | runs the whole conformance suite against the Host (see below) |
| `AdapterIntegrationE2ETests` | a real Host (real Kestrel socket) + a real `Adapter.Synthetic` in-process — two DI containers talking only over gRPC — proving each scenario end-to-end: base (all eleven types), convoy escort (route, vehicles, guaranteed ambush, SALUTE), combat outpost defense (perimeter/OPs, defend task, guaranteed ground assault, SITREP), blue force patrol (one adapter, one channel, two runners on one shared instance, feeding `BlueForceTracking` + `OwnPose` at once) |

The last two layers depend on the sweepers: both blue force deletion and own-pose staleness only exist over time and only reach a client through a background sweep.

Run locally with CI's coverage gate:

```bash
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

## Two tests worth knowing about

**`SituationObjectToUpdateTests.Convert_RoundTripsEveryObjectTypeThroughTheStore`** — all eleven object types through store → update → store, requiring equality (stored-only `creation_meta_data` normalized away). `SituationObjectToUpdate` is descriptor-driven rather than eleven hand-written mappings, so this test is the only thing between it and a silent mis-mapping, and it fails loudly when the upstream contract grows a field the converter can't carry. It uses the synthetic scenario as its fixture — the one source emitting all eleven types in a cycle — and asserts every type was exercised, so it can't quietly pass while covering nothing.

**`ConformanceSuiteE2ETests`** — runs the suite against this repo's own Host five ways:

1. normally, requiring every check to pass;
2. against a Host that rejects every write, requiring a **required** check (not merely an advisory one) to fail;
3. in `--read-only` mode, asserting the situation is byte-for-byte unchanged;
4. asserting a full run leaves nothing behind — what makes the suite safe to repeat against a live endpoint;
5. asserting all three services are covered by a default run — a suite that quietly stopped checking two of them would pass *more* easily.

A checker that fails its own reference implementation is worthless pointed at somebody else's; one that passes everything is worse. Case 2 uses a one-second stream timeout: against a badly broken implementation each streaming check costs exactly one timeout, and the default ten seconds would make the test take half a minute.

## Parallelism and metrics

xUnit runs test classes in parallel and a `MeterListener` filters by meter. Both the test recorder and the Host's `MetricsCollector` therefore filter by **meter instance**, never by name — several simulators share the test process, and name-based filtering made each scrape include every other one's measurements. That bug only ever showed up in a full run, never with the metrics tests alone.
