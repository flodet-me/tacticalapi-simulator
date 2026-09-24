# Architecture

## Design principles

- **The proto model IS the model.** The generated `Rheinmetall.TacticalApi.V0` types are used everywhere — in the store, on the event bus, and in the data sources. There is deliberately no internal abstract domain model, because the point of the simulator is to exercise the interface contract itself.
- **Server and adapters are separate executables.** `TacticalApi.Simulator.Host` is the simulated TacticalAPI service - the stores, all three gRPC services of the contract (`Situation`, `BlueForceTracking`, `OwnPose`), the map UI - and nothing else; it has no simulation sources at all. Each data source lives in its own `TacticalApi.Simulator.Adapter.*` executable (`Adapter.OpenSky`, `Adapter.Nws`, `Adapter.Synthetic`) that pushes `UpdateSituationObject`/`DeleteSituationObject` batches through `ISituationIngest`, implemented by `GrpcSituationIngest` as a genuine `Situation.SituationClient` call against whatever endpoint `Adapter:Ingest:Address` points at (see [Configuration](CONFIGURATION.md)). A source feeding one of the other two services does the same through `IBlueForceIngest`/`IOwnPoseIngest`, on the same channel and the same one setting. By default that's the Host's own native gRPC endpoint, so running the Host plus any adapter "just works", but repointing that one setting per adapter drives it against any other implementation of the TacticalAPI contract instead - independently of the others, since each adapter is its own process with its own config. Internally, each of the Host's gRPC services applies incoming RPCs straight to its own store - there's still exactly one store per service and one place writes are validated, it's just reached over the wire now instead of through a shared C# interface. Every adapter's entire `Program.cs` is a single call to `AdapterHost.Run` (in Core) - a plain generic `Host`, no ASP.NET Core, no port ever bound, since an adapter has no web surface of its own.
- **Runtime-only state.** `SituationStore`, `BlueForceStore` and `OwnPoseStore` are concurrent in-memory maps. Restart = empty situation. Nothing is ever persisted *by* the store; if you want a situation to happen twice, you record the traffic and replay it (see below), which keeps the store itself free of any notion of durability.
- **Record and replay live outside the store.** A recording is captured either on an adapter's write path (`Adapter:Recording`, lossless) or by subscribing to any endpoint's read path (`Adapter:Recorder`, works against implementations that aren't this one), and replayed by pushing it back through the ordinary ingest client. No part of the Host knows a recording exists - see [`Sources.Replay`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md). Recordings cover `Situation` traffic only: a recorded frame is a batch of `UpdateSituationObject`/`DeleteSituationObject`, and blue force keep-alives and position reports are deliberately not in it, because replaying a keep-alive out of its original time base would assert a blue force is alive when the recording says only that it once was.
- **Everything non-contract is off the contract.** Fault injection, the control endpoints and `/metrics` are all Host features, and none of them appear on any of the three services. A simulator whose gRPC surface carried extra RPCs would let a client depend on something no real implementation offers, which would make it a worse simulator - so the gRPC surface stays exactly the contract and the extras sit on plain HTTP paths beside it. The other half of the same rule: every service the contract *does* declare is implemented, because a stand-in that answers one of three is one a client can only half-integrate against.
- **Configuration via `IOptionsMonitor`.** All options are bound from each executable's own `appsettings.json`, validated with data annotations at startup (`ValidateOnStart`), and hot-reloadable: source intervals, track counts, channel capacities etc. are re-read every cycle, so you can edit `appsettings.json` while a process runs. If that file is missing, `AppSettingsBootstrap` (Core) writes it out from the executable's own embedded copy before configuration loads, so there's always a real file to edit (see [Configuration](CONFIGURATION.md)).
- **No security features** (per requirement): plain h2c (HTTP/2 without TLS), no authentication, no authorization. This is also why the control endpoints are simply on by default: gating an unauthenticated mutation surface behind a config flag, on a server that has no authentication anywhere, would be security theatre rather than security.

## Solution layout

```
src/
  simulator/
    TacticalApi.Simulator.Contracts     protoc/Grpc.Tools code generation (model + service stubs)
    TacticalApi.Simulator.Core          stores (situation, blue force, own pose), merge logic, event
                                        brokers, ingest clients, AdapterHost, options, metrics,
                                        pause switch, recording format
    TacticalApi.Simulator.Host          ASP.NET Core gRPC server (stores + Situation, BlueForceTracking
                                        and OwnPose services + map UI + control endpoints
                                        + fault injection + /metrics)
  adapter/
    TacticalApi.Simulator.Sources       shared track mapping: TrackReport, TrackUpdateFactory, TrackEmitterOptions
    TacticalApi.Simulator.Sources.OpenSky    live OpenSky Network flight tracker — see its README
    TacticalApi.Simulator.Sources.Synthetic  offline air-track, scenario and blue force patrol sources
                                             — see its README
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
- [`Sources.Synthetic/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture, scripted military scenarios (base, convoy escort, combat outpost defense) and the blue force patrol. The convoy, the outpost and the patrol all feed `BlueForceTracking`; the patrol additionally feeds `OwnPose`. Which service a thing goes on follows one rule: a friendly element reporting its own position is a blue force, anything reported *about* is a situation object
- [`Sources.Nws/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts
- [`Sources.Replay/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) — recording and replaying a situation
- [`Tool.Conformance/README.md`](../src/tools/TacticalApi.Simulator.Tool.Conformance/README.md) — the contract conformance suite

## Interface semantics implemented

### `Situation`

- `GetSituationObjects` returns all non-deleted objects.
- `SubscribeSituationObjectEvents` first streams the full snapshot (batched), then live changes.
- `AddOrUpdateSituationObjects` merges per the contract: an omitted `UpdateProperty*` leaves the stored value untouched; a present one replaces it (content may be null to clear). Each written property gets fresh `CreationMetaData` from the update's reporter/reporting time. Per-object last-write-wins: updates with an older `reporting_time` than the stored one are ignored.
- `DeleteSituationObjects` marks objects deleted (`is_deleted`), and deleted objects disappear from snapshots but are still announced on the event stream. A delete is a soft delete of an object, not a tombstone on its identity: `is_deleted` merges by the same last-write-wins rule as every other property, so a later add/update for that identity brings the object back, while one from before the delete leaves it deleted. Without that, an identity would be unusable for the rest of the process's life — the writes would be accepted, merged and even announced on the stream, and the object would stay invisible in every snapshot.
- Objects whose `expiry_time` has passed are automatically marked deleted by a background sweeper.

Every situation object type in the contract has a registered `ISituationObjectMerger` (see [Extending](EXTENDING.md#adding-support-for-more-situation-object-types) for how to add another).

### `BlueForceTracking`

- `GetBlueForces` returns every blue force currently available.
- `SubscribeBlueForceEvents` first streams all existing blue forces (batched), then live changes — including an implicit deletion, announced once with `is_deleted` set.
- `AddOrUpdateBlueForces` **replaces** each addressed blue force whole; it does not merge. This is the one place the two write paths of the contract genuinely disagree, and the contract says so outright: "In contrast to the UpdateSituationObject message all fields must be filled in every call since blue forces are usually not updated by two systems at the same time." There is consequently no per-property `CreationMetaData` and no merger here — an omitted field means the blue force no longer has one. Last-write-wins is per blue force, on `last_contact_time`, the only ordering the message carries.
- Deletion is implicit only: there is no delete RPC, so `BlueForceTimeoutSweeper` deletes a blue force once `Simulator:BlueForce:KeepAliveTimeout` elapses without a keep-alive. The tombstone is announced once and then dropped rather than kept forever — a client that missed the announcement learns the same thing from its absence in the next snapshot, and a long run with churn doesn't leak.
- `own_blue_force` and `associated_organization_unit_identity` have no field in `UpdateBlueForce` at all, so they are the answering system's to decide rather than the reporter's. The simulator sets the first from `Simulator:BlueForce:OwnIdentity` and leaves the second unset, since nothing in the contract can tell it one.

### `OwnPose`

- `UpdatePosition` records a fix per `source_identifier` (mandatory); `GetPosition` and `SubscribePositionChangedEvents` return only the one selected as primary — `Simulator:OwnPose:PrimarySource`, defaulting to whichever source reported most recently.
- A primary position that stops being refreshed keeps its coordinates and gains `is_invalid_or_expired`, matching the contract's own example of a GNSS fix going stale "because the user entered a building". `OwnPoseStalenessSweeper` exists so that flip reaches a subscriber, who by definition is not calling `GetPosition` and would otherwise hold a fix that quietly stopped being true.
- `GetPosition` before any source has reported answers successfully with no position. Failing the call would claim the request was bad; the contract allows the position itself to be absent.

### Shared across all three

- Pause (`SimulationPause`) is enforced in each store, so freezing the simulator freezes every service. A situation frozen while blue forces kept moving would not be frozen.
- Reset (`/api/control/reset`) clears all three stores: a situation emptied while blue forces kept reporting from the previous run is a state no restart produces.
- The three streams share their channel mechanics (`EventBroker<T>` in Core) and their `Simulator:Performance` settings, but count onto separate instruments — a blue force re-reports itself on a keep-alive cadence whether or not anything moved, and folding those writes in with situation traffic would swamp the number that says how busy the actual picture is.

`SituationObjectToUpdate` (Core) is the inverse of a merger: it turns a stored `SituationObject` back into the `UpdateSituationObject` that would produce it, which is what lets a recording be captured from the read side of the contract. It is driven by the protobuf descriptors rather than eleven hand-written mappings — the two message families are generated from the same contract and mirror each other field for field, so matching them by name means a field added upstream is carried across automatically instead of being silently dropped. For a repo whose CI already watches the upstream contract for drift, that is the point. `SituationObjectToUpdateTests` drives all eleven types through store → update → store and requires the two situations to come out equal.

## Beyond the contract

Three Host features exist to make the simulator useful as a test target rather than only as a stand-in. None of them is reachable through the `Situation` service.

- **Fault injection** (`Simulator:Faults`, `Host/Faults/`). One set of switches for all three services, not one per service: a client integrating against this contract talks to all of them, and a fault run that misbehaved on one while the others stayed perfect would be testing a situation that cannot happen to a real server. Latency and RPC-level errors apply to every call and live in a gRPC `Interceptor` — they are things that happen *to* a call, not properties of any particular RPC. The faults that depend on what an RPC means (an unsuccessful response header, a write acknowledged but not applied, a subscription cut short) live in `SituationGrpcService`, the only place that knows what those words mean. A stream is cut by a timer on a linked cancellation token rather than a deadline checked per batch, so an *idle* stream — the case reconnect logic is least likely to have been tested against — is cut off on time instead of surviving indefinitely because nothing happened to be flowing.
- **Control endpoints** (`Simulator:Control`, `Host/Control/`). Pause, reset, and hand-inject objects. Pausing is enforced in the store, not at the endpoint, so it applies to writes arriving over gRPC too; a paused situation rejects writes with an error header (rather than swallowing them, so a client can tell the difference) and stops expiring, which is what makes it safe to inspect. Injection goes through the same `SituationStore` as every gRPC write — a shortcut past the transport, not past the semantics.
- **Metrics** (`/metrics`, `Core/Diagnostics/` + `Host/Diagnostics/`). A plain BCL `Meter`, rendered as Prometheus text by a `MeterListener` in the Host. No OpenTelemetry dependency: the goal was to stop `SubscriberChannelFullMode` dropping events unobserved, and that doesn't justify a collector stack in an image that otherwise depends on nothing but gRPC — anyone who wants OTLP can point a collector at the meter name unchanged. The drop counter is the reason this exists at all: with the default `DropOldest`, `TryWrite` returns `true` while silently discarding an older event, so a slow subscriber lost data with nothing anywhere to show for it.
