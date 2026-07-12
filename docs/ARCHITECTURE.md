# Architecture

## Design principles

- **The proto model IS the model.** The generated `Rheinmetall.TacticalApi.V0` types are used everywhere — in the store, on the event bus, and in the data sources. There is deliberately no internal abstract domain model, because the point of the simulator is to exercise the interface contract itself.
- **One write path.** The gRPC `AddOrUpdateSituationObjects`/`DeleteSituationObjects` RPCs and every simulation source go through the same `ISituationIngest` interface with `UpdateSituationObject` messages. A source is indistinguishable from an external client.
- **Runtime-only state.** `SituationStore` is a concurrent in-memory map. Restart = empty situation.
- **Configuration via `IOptionsMonitor`.** All options are bound from `appsettings.json`, validated with data annotations at startup (`ValidateOnStart`), and hot-reloadable: source intervals, track counts, channel capacities etc. are re-read every cycle, so you can edit `appsettings.json` while the simulator runs.
- **No security features** (per requirement): plain h2c (HTTP/2 without TLS), no authentication, no authorization.

## Solution layout

```
src/
  TacticalApi.Simulator.Contracts     protoc/Grpc.Tools code generation (model + service stubs)
  TacticalApi.Simulator.Core          store, merge logic, event broker, source abstraction, options
  TacticalApi.Simulator.Sources       shared track mapping: TrackReport, TrackUpdateFactory, TrackEmitterOptions
  TacticalApi.Simulator.Sources.OpenSky    live OpenSky Network flight tracker — see its README
  TacticalApi.Simulator.Sources.Synthetic  offline air-track + scenario sources — see its README
  TacticalApi.Simulator.Sources.Nws        live NWS weather alerts (text + symbol + sketch) — see its README
  TacticalApi.Simulator.Host           ASP.NET Core gRPC host
tests/
  TacticalApi.Simulator.Tests         xUnit tests for store, broker, mapping, sources
  TacticalApi.Simulator.E2ETests      real host + real gRPC over an in-memory transport
```

Dependency direction: `Host → Sources.* → Core → Contracts`. Central package management (`Directory.Packages.props`) pins all NuGet versions in one place; shared compiler settings live in `Directory.Build.props` (nullable, warnings-as-errors, analyzers).

Per-source implementation detail lives with the source, not here:

- [`Sources.OpenSky/README.md`](../src/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker
- [`Sources.Synthetic/README.md`](../src/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and the all-object-types scenario
- [`Sources.Nws/README.md`](../src/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts

## Interface semantics implemented

- `GetSituationObjects` returns all non-deleted objects.
- `SubscribeSituationObjectEvents` first streams the full snapshot (batched), then live changes.
- `AddOrUpdateSituationObjects` merges per the contract: an omitted `UpdateProperty*` leaves the stored value untouched; a present one replaces it (content may be null to clear). Each written property gets fresh `CreationMetaData` from the update's reporter/reporting time. Per-object last-write-wins: updates with an older `reporting_time` than the stored one are ignored.
- `DeleteSituationObjects` marks objects deleted (`is_deleted`), and deleted objects disappear from snapshots but are still announced on the event stream.
- Objects whose `expiry_time` has passed are automatically marked deleted by a background sweeper.

Every situation object type in the contract has a registered `ISituationObjectMerger` (see [Extending](EXTENDING.md#adding-support-for-more-situation-object-types) for how to add another).
