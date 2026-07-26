# TacticalAPI Simulator

A simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface (`rheinmetall.tactical_api.v0.Situation`).

It implements all four RPCs of the `Situation` service against a purely in-memory situation store — no database, no persistence, everything lives for the runtime of the process.
Simulated data sources (a synthetic air picture, a live [OpenSky Network](https://opensky-network.org/) flight tracker, and live [US National Weather Service](https://www.weather.gov/documentation/services-web-api) alerts) feed the situation using the unmodified TacticalAPI data model, pushed over a real gRPC client (`Situation.SituationClient`) - the same sources can drive any other implementation of the TacticalAPI contract by repointing `Simulator:Ingest:Address` (see [Configuration](docs/CONFIGURATION.md)) at it instead.

## Running

```bash
dotnet run --project src/TacticalApi.Simulator.Host
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

With default settings the synthetic scenario source (see [`Sources.Synthetic`'s README](src/TacticalApi.Simulator.Sources.Synthetic/README.md)) immediately populates the situation; the subscribe stream shows it updating every 5 seconds.

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — design principles, solution layout, interface semantics implemented
- [Configuration](docs/CONFIGURATION.md) — `appsettings.json` reference
- [Extending the simulator](docs/EXTENDING.md) — adding a data source, adding a situation object type
- [Testing](docs/TESTING.md) — unit/E2E test layers, running coverage locally
- [CI](docs/CI.md) — pipeline stages, running the whole pipeline locally with `act`

Per-source configuration and behavior:

- [`Sources.OpenSky`](src/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker
- [`Sources.Synthetic`](src/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and the all-object-types scenario
- [`Sources.Nws`](src/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts (text + location + warning-area sketch from one feed)
