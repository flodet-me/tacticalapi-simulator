# Architecture

## Design principles

| Principle | Why |
| --- | --- |
| **The proto model IS the model.** Generated `Rheinmetall.TacticalApi.V0` types in store, bus and sources; no internal domain model. | The point is to exercise the contract itself, not a translation of it. |
| **Host and adapters are separate executables.** The Host is stores + the three gRPC services + map UI, and has no sources. Each source runs in an `Adapter.*` process and pushes through `ISituationIngest` / `IBlueForceIngest` / `IOwnPoseIngest` — real gRPC client calls at `Adapter:Ingest:Address`. | Repointing that one setting drives any other TacticalAPI implementation, per adapter, independently. |
| **Runtime-only state.** `SituationStore`, `BlueForceStore`, `OwnPoseStore` are concurrent in-memory maps. Restart = empty. | Want a situation twice? Record and replay it — the store keeps no notion of durability. |
| **Record/replay live outside the store** — an adapter write-path decorator (`Adapter:Recording`) or a read-path subscriber (`Adapter:Recorder`). `Situation` traffic only. | No Host code knows a recording exists. Replaying a keep-alive out of its time base would assert a blue force is alive when the recording says only that it once was. |
| **Everything non-contract is off the contract.** Faults, control endpoints and `/metrics` are Host HTTP paths. Every service the contract declares *is* implemented. | An extra `rpc` lets a client depend on what no real implementation offers; answering one of three services is one a client can only half-integrate against. |
| **Configuration via `IOptionsMonitor`** — data-annotation validated, `ValidateOnStart`, re-read every cycle. `AppSettingsBootstrap` (Core) writes a missing file from the embedded copy. | Edit `appsettings.json` while a process runs; there is always a real file to edit. |
| **No security features** (per requirement): plain h2c, no auth. | Control endpoints are therefore simply on — gating an unauthenticated mutation surface on a server with no authentication is theatre. |

Every adapter's `Program.cs` is one call to `AdapterHost.Run` (Core): a plain generic `Host`, no ASP.NET Core, no port bound.

## Solution layout

```text
src/
  simulator/
    …Contracts     protoc/Grpc.Tools codegen (model + service stubs)
    …Core          stores, merge logic, event brokers, ingest clients, AdapterHost,
                   options, metrics, pause switch, recording format
    …Host          ASP.NET Core gRPC server: stores + all three services + map UI
                   + control endpoints + fault injection + /metrics
  adapter/
    …Sources             shared track mapping: TrackReport, TrackUpdateFactory, TrackEmitterOptions
    …Sources.OpenSky     live OpenSky Network flight tracker          ┐
    …Sources.Synthetic   air-track, scenarios, blue force patrol      │ see each
    …Sources.Nws         live NWS weather alerts                      │ source's README
    …Sources.Replay      situation recorder + replay player           ┘
    …Adapter.{OpenSky,Synthetic,Nws,Replay}   one executable per source
  tools/
    …Tool.Conformance    checks any TacticalAPI implementation against the contract
tests/
  …Tests        xUnit: store, broker, mapping, sources, Core DI composition
  …E2ETests     real host + real adapters + real gRPC sockets
```

`src/simulator/` is the simulated service; `src/adapter/` is everything that feeds it — grouped that way because `Sources.*` is referenced only by an `Adapter.*`, never by the Host.

```text
 Host → Core → Contracts            Adapter.* → Sources.* → Core → Contracts
```

`Directory.Packages.props` pins every NuGet version; `Directory.Build.props` holds shared compiler settings (nullable, warnings-as-errors, analyzers, and `RunWorkingDirectory` so `dotnet run --project <path>` finds that project's own `appsettings.json`).

Per-source detail lives with the source: [OpenSky](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) · [Synthetic](../src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) · [Nws](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) · [Replay](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) · [Tool.Conformance](../src/tools/TacticalApi.Simulator.Tool.Conformance/README.md).

## Interface semantics implemented

### `Situation`

| RPC / behavior | Semantics |
| --- | --- |
| `GetSituationObjects` | All non-deleted objects. |
| `SubscribeSituationObjectEvents` | Full snapshot first (batched), then live changes. |
| `AddOrUpdateSituationObjects` | Merges: an omitted `UpdateProperty*` leaves the stored value; a present one replaces it (null content clears). Each written property gets fresh `CreationMetaData` from the update's reporter/time. Per-object last-write-wins on `reporting_time`. |
| `DeleteSituationObjects` | Soft delete of the *object*, not a tombstone on its identity: gone from snapshots, still announced on the stream. `is_deleted` merges last-write-wins like any property, so a newer update revives it and an older one does not. |
| Expiry | A background sweeper marks objects deleted once `expiry_time` passes. |

Without the revive rule an identity would be unusable for the rest of the process's life — writes accepted, merged and announced, object invisible in every snapshot. Every object type has a registered `ISituationObjectMerger`; see [Extending](EXTENDING.md#adding-support-for-more-situation-object-types).

### `BlueForceTracking`

| RPC / behavior | Semantics |
| --- | --- |
| `GetBlueForces` | Every blue force currently available. |
| `SubscribeBlueForceEvents` | All existing blue forces (batched), then live changes — including implicit deletion, announced once with `is_deleted`. |
| `AddOrUpdateBlueForces` | **Replaces** each blue force whole. No per-property `CreationMetaData`, no merger: an omitted field means the blue force no longer has one. Last-write-wins on `last_contact_time`, the only ordering the message carries. |
| Deletion | Implicit only — no delete RPC. `BlueForceTimeoutSweeper` deletes after `Simulator:BlueForce:KeepAliveTimeout` without a keep-alive; the tombstone is announced once, then dropped. |
| Own identity | `own_blue_force` / `associated_organization_unit_identity` have no field in `UpdateBlueForce`, so they are the answering system's: the first from `Simulator:BlueForce:OwnIdentity`, the second unset. |

The replace rule is the one place the contract's two write paths genuinely disagree, and it says so outright: *"In contrast to the UpdateSituationObject message all fields must be filled in every call since blue forces are usually not updated by two systems at the same time."* Dropping the tombstone rather than keeping it forever means a client that missed the announcement learns the same from its absence in the next snapshot, and a long run with churn does not leak.

### `OwnPose`

| RPC / behavior | Semantics |
| --- | --- |
| `UpdatePosition` | Records a fix per `source_identifier` (mandatory). |
| `GetPosition` / `SubscribePositionChangedEvents` | Return only the primary — `Simulator:OwnPose:PrimarySource`, default whichever reported most recently. |
| Staleness | A primary that stops being refreshed keeps its coordinates and gains `is_invalid_or_expired` (`OwnPoseStalenessSweeper`) — the contract's own example of a GNSS fix going stale "because the user entered a building". |
| No position yet | `GetPosition` succeeds with no position. Failing would claim the request was bad; the contract allows the position to be absent. |

The sweeper exists so the flip reaches a subscriber, who by definition is not calling `GetPosition` and would otherwise hold a fix that quietly stopped being true.

### Shared across all three

| Mechanism | Rule |
| --- | --- |
| Pause (`SimulationPause`) | Enforced in each store — a situation frozen while blue forces kept moving would not be frozen. |
| Reset (`/api/control/reset`) | Clears all three stores; a situation emptied while blue forces report on from the previous run is a state no restart produces. |
| Streams | Share channel mechanics (`EventBroker<T>`) and `Simulator:Performance`, count onto **separate** instruments — keep-alive traffic would swamp the situation numbers. |

`SituationObjectToUpdate` (Core) is the inverse of a merger — stored `SituationObject` back into the `UpdateSituationObject` that produces it, which is what lets a recording be captured from the read side. It is descriptor-driven rather than eleven hand-written mappings: the two message families are generated from the same contract and mirror each other field for field, so matching by name carries a new upstream field across instead of silently dropping it. `SituationObjectToUpdateTests` round-trips all eleven types store → update → store.

## Beyond the contract

Three Host features make the simulator a test *target*, not only a stand-in. None is reachable through any of the three services.

| Feature | Where | Design notes |
| --- | --- | --- |
| **Fault injection** | `Simulator:Faults`, `Host/Faults/` | One set of switches for all three services — a client talks to all of them, and a run that misbehaved on one while the others stayed perfect tests a situation no real server produces. Latency and RPC errors happen *to* a call, so they live in a gRPC `Interceptor`; meaning-dependent faults (unsuccessful response header, write acked but not applied, subscription cut short) live in `SituationGrpcService`. A stream is cut by a timer on a linked cancellation token, not a per-batch deadline, so an *idle* stream — the case reconnect logic is least tested against — is cut on time. |
| **Control endpoints** | `Simulator:Control`, `Host/Control/` | Pause, reset, hand-inject. Pausing is enforced in the store, so it covers gRPC writes too; a paused situation rejects writes with an error header (not silently) and stops expiring. Injection goes through the same store as every gRPC write — a shortcut past the transport, not past the semantics. |
| **Metrics** | `/metrics`, `Core/Diagnostics/` + `Host/Diagnostics/` | A plain BCL `Meter` rendered as Prometheus text by a `MeterListener`. No OpenTelemetry dependency — the goal was to stop `SubscriberChannelFullMode` dropping events unobserved, which does not justify a collector stack; point a collector at the meter name if you want OTLP. With the default `DropOldest`, `TryWrite` returns `true` while discarding an older event, so a slow subscriber lost data with nothing to show for it. |
