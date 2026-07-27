# TacticalAPI Simulator

A simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface (`rheinmetall.tactical_api.v0.Situation`).

The Host (`TacticalApi.Simulator.Host`) implements all four RPCs of the `Situation` service against a purely in-memory situation store — no database, no persistence, everything lives for the runtime of the process. It has no data sources of its own.

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
```

- **gRPC-Web endpoint: `http://localhost:4268`** (HTTP/1.1) — the official Rheinmetall test client (`testclient/csharp`, which uses `GrpcWebHandler` against this exact address) works against the simulator without changes.
- **Native gRPC endpoint: `http://localhost:5100`** (HTTP/2 h2c) — with `Grpc.Net.Client` simply `GrpcChannel.ForAddress("http://localhost:5100")` (also requires `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` before creating the channel, since there's no TLS).
- Status endpoint: `http://localhost:4268/` in the browser (object count, subscriber count).
- Situation map: `http://localhost:4268/ui` — a read-only web GUI plotting the current situation objects on a map, polling `/api/objects` every 2s.
- gRPC server reflection is enabled, so `grpcurl` works out of the box:

```bash
grpcurl -plaintext localhost:5100 list
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/GetSituationObjects
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/SubscribeSituationObjectEvents
```

With `Adapter.Synthetic` running alongside the Host, its scenario source (see [`Sources.Synthetic`'s README](src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md), enabled by default) immediately populates the situation; the subscribe stream shows it updating every 5 seconds. `Adapter.OpenSky` and `Adapter.Nws` are disabled by default (see each source's own README) since they call live external APIs.

Each adapter can just as easily point at a different, real TacticalAPI implementation instead of this Host - set that adapter's own `Adapter:Ingest:Address` (e.g. `Adapter__Ingest__Address=http://some-other-host:5100`). See [Configuration](docs/CONFIGURATION.md).

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
