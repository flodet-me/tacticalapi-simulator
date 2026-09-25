# Architecture

## Design principles

- **The proto model IS the model.** The generated `Rheinmetall.TacticalApi.V0` types are used everywhere — in the store, on the event bus, and in the data sources. There is deliberately no internal abstract domain model, because the point of the simulator is to exercise the interface contract itself.
- **Server and adapters are separate executables.** `TacticalApi.Simulator.Host` is the simulated TacticalAPI service - store, `Situation` gRPC service, map UI - and nothing else; it has no simulation sources at all. Each data source lives in its own `TacticalApi.Simulator.Adapter.*` executable (`Adapter.OpenSky`, `Adapter.Nws`, `Adapter.Synthetic`) that pushes `UpdateSituationObject`/`DeleteSituationObject` batches through `ISituationIngest`, implemented by `GrpcSituationIngest` as a genuine `Situation.SituationClient` call against whatever endpoint `Adapter:Ingest:Address` points at (see [Configuration](CONFIGURATION.md)). By default that's the Host's own native gRPC endpoint, so running the Host plus any adapter "just works", but repointing that one setting per adapter drives it against any other implementation of the TacticalAPI contract instead - independently of the others, since each adapter is its own process with its own config. Internally, the Host's gRPC service (`SituationGrpcService`) applies incoming RPCs straight to the `SituationStore` - there's still exactly one store and one place writes are validated/merged, it's just reached over the wire now instead of through a shared C# interface. Every adapter's entire `Program.cs` is a single call to `AdapterHost.Run` (in Core) - a plain generic `Host`, no ASP.NET Core, no port ever bound, since an adapter has no web surface of its own.
- **Runtime-only state.** `SituationStore` is a concurrent in-memory map. Restart = empty situation. Nothing is ever persisted *by* the store; if you want a situation to happen twice, you record the traffic and replay it (see below), which keeps the store itself free of any notion of durability.
- **Record and replay live outside the store.** A recording is captured either on an adapter's write path (`Adapter:Recording`, lossless) or by subscribing to any endpoint's read path (`Adapter:Recorder`, works against implementations that aren't this one), and replayed by pushing it back through the ordinary ingest client. No part of the Host knows a recording exists - see [`Sources.Replay`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md).
- **Everything non-contract is off the contract.** Fault injection, the control endpoints and `/metrics` are all Host features, and none of them appear in the `Situation` service. A simulator whose gRPC surface carried extra RPCs would let a client depend on something no real implementation offers, which would make it a worse simulator - so the gRPC surface stays exactly the contract and the extras sit on plain HTTP paths beside it.
- **Configuration via `IOptionsMonitor`.** All options are bound from each executable's own `appsettings.json`, validated with data annotations at startup (`ValidateOnStart`), and hot-reloadable: source intervals, track counts, channel capacities etc. are re-read every cycle, so you can edit `appsettings.json` while a process runs. If that file is missing, `AppSettingsBootstrap` (Core) writes it out from the executable's own embedded copy before configuration loads, so there's always a real file to edit (see [Configuration](CONFIGURATION.md)).
- **No security features** (per requirement): plain h2c (HTTP/2 without TLS), no authentication, no authorization. This is also why the control endpoints are simply on by default: gating an unauthenticated mutation surface behind a config flag, on a server that has no authentication anywhere, would be security theatre rather than security.

## Solution layout

```
src/
  simulator/
    TacticalApi.Simulator.Contracts     protoc/Grpc.Tools code generation (model + service stubs)
    TacticalApi.Simulator.Core          store, merge logic, event broker, ingest client, AdapterHost, options,
                                        metrics, pause switch, recording format
    TacticalApi.Simulator.Host          ASP.NET Core gRPC server (store + Situation service + map UI
                                        + control endpoints + fault injection + /metrics)
  adapter/
    TacticalApi.Simulator.Sources       shared track mapping: TrackReport, TrackUpdateFactory, TrackEmitterOptions
    TacticalApi.Simulator.Sources.OpenSky    live OpenSky Network flight tracker — see its README
    TacticalApi.Simulator.Sources.Synthetic  offline air-track + scenario sources — see its README
    TacticalApi.Simulator.Sources.Nws        live NWS weather alerts (text + symbol + sketch) — see its README
    TacticalApi.Simulator.Adapter.OpenSky    runs the OpenSky source as its own executable
    TacticalApi.Simulator.Adapter.Synthetic  runs the synthetic sources as its own executable
    TacticalApi.Simulator.Adapter.Nws        runs the NWS source as its own executable
    TacticalApi.Simulator.Sources.Replay     situation recorder + replay player — see its README
    TacticalApi.Simulator.Adapter.Replay     runs the recorder/replay player as its own executable
tools/
  TacticalApi.Simulator.Tool.Conformance  checks any TacticalAPI implementation against the contract
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
- [`Sources.Replay/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) — recording and replaying a situation
- [`Tool.Conformance/README.md`](../src/tools/TacticalApi.Simulator.Tool.Conformance/README.md) — the contract conformance suite

## Interface semantics implemented

- `GetSituationObjects` returns all non-deleted objects.
- `SubscribeSituationObjectEvents` first streams the full snapshot (batched), then live changes.
- `AddOrUpdateSituationObjects` merges per the contract: an omitted `UpdateProperty*` leaves the stored value untouched; a present one replaces it (content may be null to clear). Each written property gets fresh `CreationMetaData` from the update's reporter/reporting time. Per-object last-write-wins: updates with an older `reporting_time` than the stored one are ignored.
- `DeleteSituationObjects` marks objects deleted (`is_deleted`), and deleted objects disappear from snapshots but are still announced on the event stream. A delete is a soft delete of an object, not a tombstone on its identity: `is_deleted` merges by the same last-write-wins rule as every other property, so a later add/update for that identity brings the object back, while one from before the delete leaves it deleted. Without that, an identity would be unusable for the rest of the process's life — the writes would be accepted, merged and even announced on the stream, and the object would stay invisible in every snapshot.
- Objects whose `expiry_time` has passed are automatically marked deleted by a background sweeper.

Every situation object type in the contract has a registered `ISituationObjectMerger` (see [Extending](EXTENDING.md#adding-support-for-more-situation-object-types) for how to add another).

`SituationObjectToUpdate` (Core) is the inverse of a merger: it turns a stored `SituationObject` back into the `UpdateSituationObject` that would produce it, which is what lets a recording be captured from the read side of the contract. It is driven by the protobuf descriptors rather than eleven hand-written mappings — the two message families are generated from the same contract and mirror each other field for field, so matching them by name means a field added upstream is carried across automatically instead of being silently dropped. For a repo whose CI already watches the upstream contract for drift, that is the point. `SituationObjectToUpdateTests` drives all eleven types through store → update → store and requires the two situations to come out equal.

## Beyond the contract

Three Host features exist to make the simulator useful as a test target rather than only as a stand-in. None of them is reachable through the `Situation` service.

- **Fault injection** (`Simulator:Faults`, `Host/Faults/`). Latency and RPC-level errors apply to every call and live in a gRPC `Interceptor` — they are things that happen *to* a call, not properties of any particular RPC. The faults that depend on what an RPC means (an unsuccessful response header, a write acknowledged but not applied, a subscription cut short) live in `SituationGrpcService`, the only place that knows what those words mean. A stream is cut by a timer on a linked cancellation token rather than a deadline checked per batch, so an *idle* stream — the case reconnect logic is least likely to have been tested against — is cut off on time instead of surviving indefinitely because nothing happened to be flowing.
- **Control endpoints** (`Simulator:Control`, `Host/Control/`). Pause, reset, and hand-inject objects. Pausing is enforced in the store, not at the endpoint, so it applies to writes arriving over gRPC too; a paused situation rejects writes with an error header (rather than swallowing them, so a client can tell the difference) and stops expiring, which is what makes it safe to inspect. Injection goes through the same `SituationStore` as every gRPC write — a shortcut past the transport, not past the semantics.
- **Metrics** (`/metrics`, `Core/Diagnostics/` + `Host/Diagnostics/`). A plain BCL `Meter`, rendered as Prometheus text by a `MeterListener` in the Host. No OpenTelemetry dependency: the goal was to stop `SubscriberChannelFullMode` dropping events unobserved, and that doesn't justify a collector stack in an image that otherwise depends on nothing but gRPC — anyone who wants OTLP can point a collector at the meter name unchanged. The drop counter is the reason this exists at all: with the default `DropOldest`, `TryWrite` returns `true` while silently discarding an older event, so a slow subscriber lost data with nothing anywhere to show for it.
