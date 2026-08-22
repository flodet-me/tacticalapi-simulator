# TacticalAPI Simulator

A simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface (`rheinmetall.tactical_api.v0.Situation`).

The Host (`TacticalApi.Simulator.Host`) implements all four RPCs of the `Situation` service against a purely in-memory situation store — no database, no persistence, everything lives for the runtime of the process. It has no data sources of its own.

It can also be made to misbehave on purpose, driven from outside the contract (pause/reset/inject), recorded and replayed, scraped for metrics, and used to check *other* implementations of the same contract — see [Beyond a plain stand-in](#beyond-a-plain-stand-in).

Simulated data sources (a synthetic air picture, a live [OpenSky Network](https://opensky-network.org/) flight tracker, and live [US National Weather Service](https://www.weather.gov/documentation/services-web-api) alerts) each run as their own adapter executable (`TacticalApi.Simulator.Adapter.Synthetic`/`.OpenSky`/`.Nws`), feeding the situation using the unmodified TacticalAPI data model, pushed over a real gRPC client (`Situation.SituationClient`). Each adapter can drive any other implementation of the TacticalAPI contract, independently of the others, by repointing its own `Adapter:Ingest:Address` (see [Configuration](docs/CONFIGURATION.md)) at it instead.

## Running

Requires the .NET 10 SDK. If you have [Nix](https://nixos.org/) instead (with flakes enabled), `nix develop` (or `direnv allow`, once) gets you that plus `act`, `grpcurl`, `tshark`, `jq`, `yq-go` and `python3` with no other setup - see [Nix](docs/NIX.md).

```bash
# The server: store + Situation gRPC service + map UI.
dotnet run --project src/simulator/TacticalApi.Simulator.Host

# In separate terminals, whichever data sources you want live:
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Synthetic
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Nws

# Optional: record what the situation does, or replay a recording into it.
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay
```

- **gRPC-Web endpoint: `http://localhost:4268`** (HTTP/1.1) — the official Rheinmetall test client (`testclient/csharp`, which uses `GrpcWebHandler` against this exact address) works against the simulator without changes.
- **Native gRPC endpoint: `http://localhost:5100`** (HTTP/2 h2c) — with `Grpc.Net.Client` simply `GrpcChannel.ForAddress("http://localhost:5100")` (also requires `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` before creating the channel, since there's no TLS).
- Status endpoint: `http://localhost:4268/` in the browser (object count, subscriber count).
- Situation map: `http://localhost:4268/ui` — a read-only web GUI plotting the current situation objects on a map, polling `/api/objects` every 2s.
- Metrics: `http://localhost:4268/metrics` — Prometheus text format (see [Metrics](#metrics)).
- Control: `http://localhost:4268/api/control/*` — pause, reset, inject (see [Control endpoints](#control-endpoints)).
- gRPC server reflection is enabled, so `grpcurl` works out of the box:

```bash
grpcurl -plaintext localhost:5100 list
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/GetSituationObjects
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/SubscribeSituationObjectEvents
```

With `Adapter.Synthetic` running alongside the Host, its scenario source (see [`Sources.Synthetic`'s README](src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md), enabled by default) immediately populates the situation; the subscribe stream shows it updating every 5 seconds. `Adapter.OpenSky` and `Adapter.Nws` are disabled by default (see each source's own README) since they call live external APIs.

Each adapter can just as easily point at a different, real TacticalAPI implementation instead of this Host - set that adapter's own `Adapter:Ingest:Address` (e.g. `Adapter__Ingest__Address=http://some-other-host:5100`). See [Configuration](docs/CONFIGURATION.md).

## Beyond a plain stand-in

A simulator's first job is to look like a working server. These are the parts that go further — none of them is
reachable through the `Situation` service, which stays exactly the contract and nothing else.

### Record and replay

Capture a situation to a `.jsonl` file and push it back later, optionally faster. This is what turns a bug someone
hit into a fixture you can rerun, and a live-API demo into one that works offline.

```bash
# Record what any adapter pushes (lossless - it wraps the ingest client).
Adapter__Recording__Enabled=true dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky

# Or record what a whole endpoint's situation does, whoever is feeding it.
Adapter__Recorder__Enabled=true dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay

# Replay it at 5x into whatever Adapter:Ingest:Address points at.
Adapter__Replay__Enabled=true Adapter__Replay__Speed=5 \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay
```

Recordings are one JSON object per line with the objects in canonical protobuf JSON, so `jq` is a perfectly good
recording editor. See [`Sources.Replay`](src/adapter/TacticalApi.Simulator.Sources.Replay/README.md).

### Fault injection

`Simulator:Faults` makes the Host slow, flaky, or quietly wrong, so a client can be proven to survive it: added
latency, RPC-level errors, `success = false` response headers, writes acknowledged and then discarded, and
subscriptions cut short. Options are hot-reloadable like everything else, so faults can be switched on **while a
client stays connected** and you watch it react. See [Configuration](docs/CONFIGURATION.md#simulatorfaults).

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
```

Injection goes through the same store as every gRPC write — a shortcut past the transport, not past the semantics.

### Metrics

`/metrics` in Prometheus text format, from a plain BCL `Meter` (no OpenTelemetry dependency). The one that matters:

```
tacticalapi_subscriber_events_dropped_total 18956
tacticalapi_subscriber_events_published_total 20500
```

With the default `SubscriberChannelFullMode: DropOldest`, a subscriber that can't keep up silently loses events —
`TryWrite` returns `true` while discarding an older one. That is now countable, which is what makes
`Simulator:Performance` tunable rather than guessable. `Adapter:LoadGenerator` (in `Adapter.Synthetic`, off by
default) exists to push an endpoint hard enough for those numbers to mean something.

### Checking other implementations

Every adapter can already drive any TacticalAPI implementation by repointing `Adapter:Ingest:Address`. The
conformance tool is the other direction — it *verifies* one:

```bash
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --address http://their-host:5100
```

57 checks covering merge, staleness, delete, streaming, tolerance, property shapes and expiry semantics — plus a
generated check for every case of the contract's three capability oneofs. Every hand-written check uses
`symbol` + `string_identity` + `point`, so without those an implementation handling only those three would pass
everything:

```
Object types accepted: 8 of 11
  not accepted: overlay-document, sketch-document, voice-message-document

Identity kinds accepted: 2 of 4
  not accepted: int32-identity, int64-identity

Location kinds accepted: 4 of 9
  not accepted: ellipse, fan, sketch-location, corridor, route-location
```

Each check quotes the rule it enforces and carries a severity: **required** where the contract states the rule
outright, **advisory** where the contract is silent and this is merely the reading the simulator applies. Advisory
failures print `WARN` and don't fail the run unless you pass `--strict` — telling an implementer they're
non-conformant over something the contract never mentions is the fastest way to get a tool ignored.

`--read-only` runs only the checks that never write, so it's safe against a live situation. `--junit` emits a
report every CI already renders. Exit codes: 0 conformant, 1 non-conformant, 2 bad arguments, 3 endpoint
unreachable — so a pipeline can tell "your server is down" from "your server is wrong". See
[`Tool.Conformance`](src/tools/TacticalApi.Simulator.Tool.Conformance/README.md).

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — design principles, solution layout, interface semantics implemented
- [Configuration](docs/CONFIGURATION.md) — `appsettings.json` reference
- [Extending the simulator](docs/EXTENDING.md) — adding a data source, adding a situation object type
- [Testing](docs/TESTING.md) — unit/E2E test layers, running coverage locally
- [CI](docs/CI.md) — pipeline stages, running the whole pipeline locally with `act`
- [Nix](docs/NIX.md) — the dev shell, `direnv`, and the `format`/`ci-local` apps

Per-source configuration and behavior (run via the matching `Adapter.*` project):

- [`Sources.OpenSky`](src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker (`Adapter.OpenSky`)
- [`Sources.Synthetic`](src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and scripted military scenarios (base, convoy escort, combat outpost defense) (`Adapter.Synthetic`)
- [`Sources.Nws`](src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts (text + location + warning-area sketch from one feed) (`Adapter.Nws`)
- [`Sources.Replay`](src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) — situation recorder and replay player (`Adapter.Replay`)

Tools:

- [`Tool.Conformance`](src/tools/TacticalApi.Simulator.Tool.Conformance/README.md) — checks any TacticalAPI implementation against the contract
