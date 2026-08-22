# Testing

Two test layers, both run by `dotnet test`:

- **Unit tests** (`TacticalApi.Simulator.Tests`) cover the store semantics (merge, last-write-wins, delete, expiry, object cap, reset, pause), every merger (via `AllMergers` a meta-test asserts full oneof coverage), the event broker and its drop accounting, the metrics instruments, the recording format and its writer/reader, the stored-to-update converter, the replay player, the load generator, and the track mapping. Source-specific unit test coverage is documented in each source project's own README.
- **E2E tests** (`TacticalApi.Simulator.E2ETests`) boot the *real* host via `WebApplicationFactory<Program>` and exercise it through *real* gRPC calls on an in-memory transport: add/get round-trip, partial merge over the wire, delete, error headers, snapshot-then-live-events on the subscribe stream, stale-update rejection, the gRPC-Web transport (same path as the official Rheinmetall test client), the expiry sweeper, and the HTTP status endpoint. Background sources are disabled by default in the fixture so tests stay deterministic; individual tests opt back in via configuration overrides. `ControlEndpointsE2ETests`, `FaultInjectionE2ETests` and `MetricsEndpointE2ETests` cover the Host surfaces that sit beside the contract; `RecordReplayE2ETests` records a real Host over a real subscription, resets it, replays the recording and checks the situation came back; `ConformanceSuiteE2ETests` runs the whole conformance suite against the Host. `AdapterIntegrationE2ETests` goes further, composing a real Host (real Kestrel socket) and a real `Adapter.Synthetic` in-process - two separate DI containers talking only over gRPC, exactly like running them as separate processes - to prove each synthetic scenario end-to-end over the wire: the base scenario (all eleven object types), the convoy escort (route, vehicles, a guaranteed ambush, the SALUTE report), and the combat outpost defense (perimeter/OPs, the defend task, a guaranteed ground assault, the SITREP). See `Sources.Synthetic`'s README for what each scenario models.

Run locally with the same coverage gate as CI:

```bash
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

## Two tests worth knowing about

Most of the suite is ordinary. These two carry more weight than their size suggests.

**`SituationObjectToUpdateTests.Convert_RoundTripsEveryObjectTypeThroughTheStore`** drives all eleven situation object types through store → update → store and requires the two situations to come out equal (with `creation_meta_data`, which is stored-only, normalized away). `SituationObjectToUpdate` is descriptor-driven rather than eleven hand-written mappings, so this test is the only thing standing between it and a silent mis-mapping — and it fails loudly if the upstream contract grows a field the converter can't carry. It uses the synthetic scenario as its fixture because that is the one source emitting all eleven types in a single cycle, and asserts that every type was actually exercised, so it can't quietly pass while covering nothing.

**`ConformanceSuiteE2ETests`** runs the conformance suite against this repo's own Host: once normally, requiring every check to pass; once against a Host configured to reject every write, requiring a *required* check (not merely an advisory one) to fail; once in `--read-only` mode, asserting the situation is byte-for-byte unchanged afterwards; and once checking that a full run leaves nothing behind, which is what makes the suite safe to repeat against a live endpoint. A checker that fails its own reference implementation is worthless when pointed at somebody else's; one that passes everything is worse. The second case uses a one-second stream timeout, because against a badly broken implementation each streaming check costs exactly one timeout and the default ten seconds would make the test take half a minute.

## A note on parallelism and metrics

xUnit runs test classes in parallel, and a `MeterListener` filters by meter. Both the unit-test recorder and the Host's `MetricsCollector` therefore filter by **meter instance**, not by meter name — several simulators share the test process, and name-based filtering made each one's scrape include every other one's measurements. That bug was real and only showed up in a full run, never when the metrics tests were run alone.
