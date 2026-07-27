# Architecture

## Design principles

- **The proto model IS the model.** The generated `Rheinmetall.TacticalApi.V0` types are used everywhere — in the store, on the event bus, and in the data sources. There is deliberately no internal abstract domain model, because the point of the simulator is to exercise the interface contract itself.
- **Server and adapters are separate executables.** `TacticalApi.Simulator.Host` is the simulated TacticalAPI service - store, `Situation` gRPC service, map UI - and nothing else; it has no simulation sources at all. Each data source lives in its own `TacticalApi.Simulator.Adapter.*` executable (`Adapter.OpenSky`, `Adapter.Nws`, `Adapter.Synthetic`) that pushes `UpdateSituationObject`/`DeleteSituationObject` batches through `ISituationIngest`, implemented by `GrpcSituationIngest` as a genuine `Situation.SituationClient` call against whatever endpoint `Adapter:Ingest:Address` points at (see [Configuration](CONFIGURATION.md)). By default that's the Host's own native gRPC endpoint, so running the Host plus any adapter "just works", but repointing that one setting per adapter drives it against any other implementation of the TacticalAPI contract instead - independently of the others, since each adapter is its own process with its own config. Internally, the Host's gRPC service (`SituationGrpcService`) applies incoming RPCs straight to the `SituationStore` - there's still exactly one store and one place writes are validated/merged, it's just reached over the wire now instead of through a shared C# interface. Every adapter's entire `Program.cs` is a single call to `AdapterHost.Run` (in Core) - a plain generic `Host`, no ASP.NET Core, no port ever bound, since an adapter has no web surface of its own.
- **Runtime-only state.** `SituationStore` is a concurrent in-memory map. Restart = empty situation.
- **Configuration via `IOptionsMonitor`.** All options are bound from each executable's own `appsettings.json`, validated with data annotations at startup (`ValidateOnStart`), and hot-reloadable: source intervals, track counts, channel capacities etc. are re-read every cycle, so you can edit `appsettings.json` while a process runs. If that file is missing, `AppSettingsBootstrap` (Core) writes it out from the executable's own embedded copy before configuration loads, so there's always a real file to edit (see [Configuration](CONFIGURATION.md)).
- **No security features** (per requirement): plain h2c (HTTP/2 without TLS), no authentication, no authorization.

## Solution layout

```
src/
  simulator/
    TacticalApi.Simulator.Contracts     protoc/Grpc.Tools code generation (model + service stubs)
    TacticalApi.Simulator.Core          store, merge logic, event broker, ingest client, AdapterHost, options
    TacticalApi.Simulator.Host          ASP.NET Core gRPC server (store + Situation service + map UI)
  adapter/
    TacticalApi.Simulator.Sources       shared track mapping: TrackReport, TrackUpdateFactory, TrackEmitterOptions
    TacticalApi.Simulator.Sources.OpenSky    live OpenSky Network flight tracker — see its README
    TacticalApi.Simulator.Sources.Synthetic  offline air-track + scenario sources — see its README
    TacticalApi.Simulator.Sources.Nws        live NWS weather alerts (text + symbol + sketch) — see its README
    TacticalApi.Simulator.Adapter.OpenSky    runs the OpenSky source as its own executable
    TacticalApi.Simulator.Adapter.Synthetic  runs the synthetic sources as its own executable
    TacticalApi.Simulator.Adapter.Nws        runs the NWS source as its own executable
tests/
  TacticalApi.Simulator.Tests         xUnit tests for store, broker, mapping, sources, Core DI composition
  TacticalApi.Simulator.E2ETests      real host + real adapters + real gRPC sockets
```

`src/simulator/` is the simulated TacticalAPI service itself; `src/adapter/` is everything that feeds it data - grouped this way because `Sources.*` is never referenced by the Host, only by an `Adapter.*` (see the dependency direction below).

Dependency direction: `Host → Core → Contracts` and, separately, `Adapter.* → Sources.* → Core → Contracts` - the Host never references any `Sources.*` project, and no `Adapter.*` project references another. Central package management (`Directory.Packages.props`) pins all NuGet versions in one place; shared compiler settings live in `Directory.Build.props` (nullable, warnings-as-errors, analyzers, and `RunWorkingDirectory` so `dotnet run --project <path>` finds that project's own `appsettings.json` regardless of the caller's working directory).

Per-source implementation detail lives with the source, not here:

- [`Sources.OpenSky/README.md`](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker
- [`Sources.Synthetic/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and scripted military scenarios (base, convoy escort, combat outpost defense)
- [`Sources.Nws/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts

## Interface semantics implemented

- `GetSituationObjects` returns all non-deleted objects.
- `SubscribeSituationObjectEvents` first streams the full snapshot (batched), then live changes.
- `AddOrUpdateSituationObjects` merges per the contract: an omitted `UpdateProperty*` leaves the stored value untouched; a present one replaces it (content may be null to clear). Each written property gets fresh `CreationMetaData` from the update's reporter/reporting time. Per-object last-write-wins: updates with an older `reporting_time` than the stored one are ignored.
- `DeleteSituationObjects` marks objects deleted (`is_deleted`), and deleted objects disappear from snapshots but are still announced on the event stream.
- Objects whose `expiry_time` has passed are automatically marked deleted by a background sweeper.

Every situation object type in the contract has a registered `ISituationObjectMerger` (see [Extending](EXTENDING.md#adding-support-for-more-situation-object-types) for how to add another).
