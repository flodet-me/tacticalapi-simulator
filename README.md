# TacticalAPI Simulator

A simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface — `rheinmetall.tactical_api.v0.Situation`, `.BlueForceTracking` and `.OwnPose`.

The Host implements **every** RPC of all three services against in-memory stores: no database, no persistence, nothing outlives the process. It has no data sources of its own. It can also be made to misbehave on purpose, recorded and replayed, scraped for metrics, and used to check *other* implementations — see [Beyond a plain stand-in](#beyond-a-plain-stand-in).

```text
 Adapter.Synthetic  ┐                                    Host
 Adapter.OpenSky    │  real gRPC client call             ├─ Situation
 Adapter.Nws        ├──────────────────────────────────► ├─ BlueForceTracking
 Adapter.Replay     ┘  Adapter:Ingest:Address            ├─ OwnPose
   one source per process,   (default: the Host;         └─ map UI · /metrics · /api/control
   no port of its own         repoint at any other
                              TacticalAPI impl)
```

## Running

Requires the .NET 10 SDK — or [Nix](https://nixos.org/) with flakes, where `nix develop` (or `direnv allow`, once) adds that plus `act`, `gh`, `grpcurl`, `tshark`, `jq`, `yq-go` and `python3` ([Nix](docs/NIX.md)).

```bash
# The server: stores + the three gRPC services + map UI.
dotnet run --project src/simulator/TacticalApi.Simulator.Host

# In separate terminals, whichever data sources you want live:
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Synthetic
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Nws

# Optional: record what the situation does, or replay a recording into it.
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay
```

| Endpoint | |
| --- | --- |
| **gRPC-Web `http://localhost:4268`** (HTTP/1.1) | The official Rheinmetall test client (`testclient/csharp`, `GrpcWebHandler` against this exact address) works unchanged. |
| **Native gRPC `http://localhost:5100`** (HTTP/2 h2c) | `GrpcChannel.ForAddress("http://localhost:5100")`, plus `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` before creating the channel — there is no TLS. |
| `http://localhost:4268/` | Status: object count, subscriber count. |
| `http://localhost:4268/ui` | Read-only map of the current situation, polling `/api/objects` every 2s. Blue forces are a second, separately toggleable layer from `/api/blueforces`: callsigns, a tie line from a mounted force to its carrier, a ring around the own force. |
| `http://localhost:4268/metrics` | Prometheus text format ([Metrics](#metrics)). |
| `http://localhost:4268/api/control/*` | Pause, reset, inject ([Control endpoints](#control-endpoints)). |

gRPC server reflection is on, so `grpcurl` works out of the box:

```bash
grpcurl -plaintext localhost:5100 list
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/GetSituationObjects
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/SubscribeSituationObjectEvents
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.BlueForceTracking/GetBlueForces
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.OwnPose/GetPosition
```

With `Adapter.Synthetic` alongside the Host, its scenario source (enabled by default, see [`Sources.Synthetic`](src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md)) populates the situation immediately and the subscribe stream updates every 5s; its blue force patrol is on too, so `BlueForceTracking` and `OwnPose` come up populated rather than empty. `Adapter.OpenSky` and `Adapter.Nws` are off by default — they call live external APIs.

To drive a *different* TacticalAPI implementation, set that adapter's own `Adapter:Ingest:Address` (e.g. `Adapter__Ingest__Address=http://some-other-host:5100`). See [Configuration](docs/CONFIGURATION.md).

## Beyond a plain stand-in

A simulator's first job is to look like a working server. These go further — and none of them is reachable through the gRPC surface, which stays exactly the contract.

### Record and replay

Turns a bug someone hit into a fixture you can rerun, and a live-API demo into one that works offline.

```bash
# Record what any adapter pushes (lossless - it wraps the ingest client).
Adapter__Recording__Enabled=true dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky

# Or record what a whole endpoint's situation does, whoever is feeding it.
Adapter__Recorder__Enabled=true dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay

# Replay it at 5x into whatever Adapter:Ingest:Address points at.
Adapter__Replay__Enabled=true Adapter__Replay__Speed=5 \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay
```

One JSON object per line, objects in canonical protobuf JSON — `jq` is a perfectly good recording editor. See [`Sources.Replay`](src/adapter/TacticalApi.Simulator.Sources.Replay/README.md).

### Fault injection

`Simulator:Faults` makes the Host slow, flaky or quietly wrong, so a client can be *proven* to survive it: added latency, RPC errors, `success = false` response headers, writes acknowledged then discarded, subscriptions cut short. Hot-reloadable like everything else, so faults switch on **while a client stays connected** and you watch it react. See [Configuration](docs/CONFIGURATION.md#simulatorfaults).

### Control endpoints

```bash
curl http://localhost:4268/api/control/state                 # paused? object count? which faults are armed?
curl -X POST http://localhost:4268/api/control/pause         # freeze the situation (writes rejected, nothing expires)
curl -X POST http://localhost:4268/api/control/resume
curl -X POST http://localhost:4268/api/control/reset         # drop every object
curl -X POST http://localhost:4268/api/control/objects \
  -d '{"situationObjects":[{"symbol":{"identity":{"stringIdentity":"x"},
       "reporter":{"stringIdentity":"me"},"reportingTime":"2026-08-22T07:00:00Z",
       "name":{"content":"HAND INJECTED"}}}]}'               # inject by hand (protobuf JSON, same as grpcurl -d)

curl -X POST http://localhost:4268/api/control/blueforces \
  -d '{"blueForcesToUpdates":[{"identity":{"stringIdentity":"bf:x"},
       "lastContactTime":"2026-08-22T07:00:00Z","callsign":"HAND"}]}'   # inject a blue force
curl -X POST http://localhost:4268/api/control/position \
  -d '{"position":{"sourceIdentifier":"HAND","pointLocation":
       {"geoPoint":{"latitudeCoordinate":53.08,"longitudeCoordinate":8.8}}}}'  # set the own position
```

`reset` and `pause` cover all three services — a situation frozen while blue forces kept moving would not be frozen. Injection goes through the same store as every gRPC write: a shortcut past the transport, not past the semantics.

### Metrics

`/metrics` in Prometheus text format, from a plain BCL `Meter` (no OpenTelemetry dependency). The pair that matters:

```text
tacticalapi_subscriber_events_dropped_total 18956
tacticalapi_subscriber_events_published_total 20500
```

With the default `SubscriberChannelFullMode: DropOldest` a slow subscriber silently loses events — `TryWrite` returns `true` while discarding an older one. Counting it is what makes `Simulator:Performance` tunable rather than guessable; `Adapter:LoadGenerator` (in `Adapter.Synthetic`, off by default) pushes an endpoint hard enough for the numbers to mean something.

`BlueForceTracking` and `OwnPose` count onto instruments of their own (`tacticalapi_blue_forces`, `…_blue_forces_updated_total`, `…_blue_forces_expired_total`, `…_positions_updated_total`, and a `*_events_published/dropped_total` pair per stream) rather than being folded in above — a blue force re-reports itself on a keep-alive cadence whether or not anything moved, which would swamp the number that says how busy the actual picture is.

### Checking other implementations

Every adapter can already *drive* any implementation. The conformance tool is the other direction — it verifies one:

```bash
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --address http://their-host:5100
```

79 checks across all three services. For `Situation`: merge, staleness, delete, streaming, tolerance, property shapes and expiry, plus a generated check per case of its three capability oneofs. Every hand-written check uses `symbol` + `string_identity` + `point`, so without the generated ones an implementation handling only those three would pass everything:

```text
Object types accepted: 8 of 11
  not accepted: overlay-document, sketch-document, voice-message-document

Identity kinds accepted: 2 of 4
  not accepted: int32-identity, int64-identity

Location kinds accepted: 4 of 9
  not accepted: ellipse, fan, sketch-location, corridor, route-location
```

| | |
| --- | --- |
| Severity | Each check quotes the rule it enforces. **required** = the contract states it outright; **advisory** = the contract is silent and this is merely the simulator's reading. Advisory failures print `WARN` and don't fail the run without `--strict` — calling an implementer non-conformant over something the contract never mentions is the fastest way to get a tool ignored. |
| The `BlueForceTracking` check to care about | *"all fields must be filled in every call"*. An implementation that quietly merges blue force updates the way it merges situation objects passes everything else, then in the field keeps a callsign or a mount host alive long after its sender stopped reporting it. |
| Why `OwnPose` is mostly advisory | The position handed back is "the one selected as primary position source by the application", so an implementation may accept a fix and keep answering with a different sensor's. |
| No cleanup, and it says so first | The contract gives `BlueForceTracking` no delete RPC and `OwnPose` no way to un-report a position, so what those checks write ages out on the implementation's own keep-alive timeout. |
| Flags | `--read-only` runs only non-writing checks (safe against a live situation); `--junit` emits a report every CI renders. |
| Exit codes | `0` conformant · `1` non-conformant · `2` bad arguments · `3` endpoint unreachable — a pipeline can tell "your server is down" from "your server is wrong". |

See [`Tool.Conformance`](src/tools/TacticalApi.Simulator.Tool.Conformance/README.md).

## Documentation

| Doc | |
| --- | --- |
| [Architecture](docs/ARCHITECTURE.md) | design principles, solution layout, interface semantics implemented |
| [Configuration](docs/CONFIGURATION.md) | `appsettings.json` reference |
| [Extending](docs/EXTENDING.md) | adding a data source, an object type, a blue force or own-position source |
| [Testing](docs/TESTING.md) | unit/E2E layers, running coverage locally |
| [CI](docs/CI.md) | pipeline stages, running the whole thing locally with `act` |
| [Nix](docs/NIX.md) | the dev shell, `direnv`, the `format`/`ci-local` apps |

Per-source behavior and configuration (run via the matching `Adapter.*`):

| Source | |
| --- | --- |
| [`Sources.OpenSky`](src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) | live OpenSky Network flight tracker |
| [`Sources.Synthetic`](src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) | offline air-track picture and scripted military scenarios (base, convoy escort, combat outpost defense) |
| [`Sources.Nws`](src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) | live US National Weather Service alerts (text + location + warning-area sketch from one feed) |
| [`Sources.Replay`](src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) | situation recorder and replay player |
| [`Tool.Conformance`](src/tools/TacticalApi.Simulator.Tool.Conformance/README.md) | checks any TacticalAPI implementation against the contract |
