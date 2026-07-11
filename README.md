# TacticalAPI Simulator

A .NET 10 / C# simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface (`rheinmetall.tactical_api.v0.Situation`).

It implements all four RPCs of the `Situation` service against a purely in-memory situation store — no database, no persistence, everything lives for the runtime of the process.
Simulated data sources (a synthetic air picture and a live [OpenSky Network](https://opensky-network.org/) flight tracker) feed tracks into the situation using the unmodified TacticalAPI data model.

## Design principles

- **The proto model IS the model.** The generated `Rheinmetall.TacticalApi.V0` types are used everywhere — in the store, on the event bus, and in the data sources. There is deliberately no internal abstract domain model, because the point of the simulator is to exercise the interface contract itself.
- **One write path.** The gRPC `AddOrUpdateSituationObjects`/`DeleteSituationObjects` RPCs and every simulation source go through the same `ISituationIngest` interface with `UpdateSituationObject` messages. A source is indistinguishable from an external client.
- **Runtime-only state.** `SituationStore` is a concurrent in-memory map. Restart = empty situation.
- **Configuration via `IOptionsMonitor`.** All options are bound from `appsettings.json`, validated with data annotations at startup (`ValidateOnStart`), and hot-reloadable: source intervals, track counts, channel capacities etc. are re-read every cycle, so you can edit `appsettings.json` while the simulator runs.
- **No security features** (per requirement): plain h2c (HTTP/2 without TLS), no authentication, no authorization.

## Solution layout

```
protos/                          .proto contract, copied verbatim from Rheinmetall/tacticalapi
src/
  TacticalApi.Simulator.Contracts   protoc/Grpc.Tools code generation (model + service stubs)
  TacticalApi.Simulator.Core        store, merge logic, event broker, source abstraction, options
  TacticalApi.Simulator.Sources     bundled sources: SyntheticAirTracks, OpenSky
  TacticalApi.Simulator.Host        ASP.NET Core gRPC host
tests/
  TacticalApi.Simulator.Tests       xUnit tests for store, broker, mapping, sources
```

Dependency direction: `Host → Sources.* → Core → Contracts`. Central package management (`Directory.Packages.props`) pins all NuGet versions in one place; shared compiler settings live in `Directory.Build.props` (nullable, warnings-as-errors, analyzers).

## Running

```bash
dotnet run --project src/TacticalApi.Simulator.Host
```

- **gRPC-Web endpoint: `http://localhost:4268`** (HTTP/1.1) — the official Rheinmetall test client (`testclient/csharp`, which uses `GrpcWebHandler` against this exact address) works against the simulator without changes.
- **Native gRPC endpoint: `http://localhost:5100`** (HTTP/2 h2c) — with `Grpc.Net.Client` simply `GrpcChannel.ForAddress("http://localhost:5100")`.
- Status endpoint: `http://localhost:4268/` in the browser (object count, subscriber count).
- gRPC server reflection is enabled, so `grpcurl` works out of the box:

```bash
grpcurl -plaintext localhost:5100 list
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/GetSituationObjects
grpcurl -plaintext localhost:5100 rheinmetall.tactical_api.v0.Situation/SubscribeSituationObjectEvents
```

With default settings the synthetic air source immediately populates 12 moving tracks around Bremen; the subscribe stream shows them updating every 2 seconds.

## Interface semantics implemented

- `GetSituationObjects` returns all non-deleted objects.
- `SubscribeSituationObjectEvents` first streams the full snapshot (batched), then live changes.
- `AddOrUpdateSituationObjects` merges per the contract: an omitted `UpdateProperty*` leaves the stored value untouched; a present one replaces it (content may be null to clear). Each written property gets fresh `CreationMetaData` from the update's reporter/reporting time. Per-object last-write-wins: updates with an older `reporting_time` than the stored one are ignored.
- `DeleteSituationObjects` marks objects deleted (`is_deleted`), and deleted objects disappear from snapshots but are still announced on the event stream.
- Objects whose `expiry_time` has passed are automatically marked deleted by a background sweeper.

Currently merged object types: **Symbol** and **TextDocument**. Other `oneof` cases are rejected with a descriptive error header; support is added by registering another `ISituationObjectMerger` (see below).

## Configuration

Everything lives under `Simulator` in `appsettings.json` and reloads at runtime:

```jsonc
"Simulator": {
  "ReporterId": "TacticalAPI-Simulator",
  "ExpirySweepInterval": "00:00:10",
  "Performance": {
    "SubscriberChannelCapacity": 4096,     // per-subscriber event buffer
    "SubscriberChannelFullMode": "DropOldest", // or "Wait" for backpressure
    "StreamBatchSize": 256,                // objects per streamed response
    "MaxReceiveMessageSizeMb": 16,
    "MaxSituationObjects": 100000          // memory guard (no DB!)
  },
  "Sources": {
    "SyntheticAirTracks": { "Enabled": true, "TrackCount": 12, "UpdateInterval": "00:00:02", ... },
    "OpenSky":            { "Enabled": false, "PollInterval": "00:00:15", ... }
  }
}
```

Performance-relevant behavior is configuration, not code: channel sizes, overflow strategy (`DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers), batch sizes and object caps. The host additionally runs with Server GC.

## Live data: OpenSky flight tracker

Set `Simulator:Sources:OpenSky:Enabled` to `true` and every aircraft inside the configured bounding box becomes a TacticalAPI `Symbol` with a `Point` location (position, altitude, course, speed), callsign as name, a configurable 2525C symbol code, and a TTL-based `expiry_time` so aircraft that leave the box expire automatically. Anonymous OpenSky access is rate limited — keep the poll interval at 10 s or more.

## Adding your own data source (e.g. an AIS ship tracker)

1. Implement `ISimulationSource` — fetch your data and map it to `UpdateSituationObject`. For track-like data, `TrackReport` + `TrackUpdateFactory.CreateSymbolUpdate(...)` does the TacticalAPI mapping for you:

```csharp
public sealed class AisShipSource(IHttpClientFactory http, IOptionsMonitor<AisOptions> options, TimeProvider time)
    : ISimulationSource
{
    public string Name => "AIS";
    public bool Enabled => options.CurrentValue.Enabled;
    public TimeSpan Interval => options.CurrentValue.PollInterval;

    public async Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken ct)
    {
        var ships = await FetchShipsAsync(ct);
        var now = time.GetUtcNow();
        return ships.Select(s => TrackUpdateFactory.CreateSymbolUpdate(
            new TrackReport($"ais:{s.Mmsi}", s.Name, s.Lat, s.Lon, 0, s.Course, s.SpeedMs, $"MMSI {s.Mmsi}"),
            "SIM-AIS", "SNSP-----------", SymbolCatalog.Mil2525C, now, options.CurrentValue.TrackTimeToLive)).ToList();
    }
}
```

2. Register it with its options:

```csharp
services.AddOptions<AisOptions>().Bind(config.GetSection("Simulator:Sources:Ais"))
    .ValidateDataAnnotations().ValidateOnStart();
services.AddSimulationSource<AisShipSource>();
```

Each source gets its own `SimulationSourceRunner` background service, so a slow or failing source never stalls the others; exceptions are logged and retried next cycle.

## Adding support for more situation object types

Implement `ISituationObjectMerger` for the `UpdateSituationObject` oneof case (see `SymbolMerger` as the template — it's ~40 lines) and register it:

```csharp
services.AddSingleton<ISituationObjectMerger, RouteMerger>();
```

The store discovers mergers by their `HandledCase`; no other change needed.

## CI

`.github/workflows/ci.yml`: restore → build (Release, warnings as errors) → test with coverage → publish the host as a downloadable artifact. NuGet packages are cached.
