# Configuration

Every executable has its own `appsettings.json` and reloads at runtime (see [Architecture](ARCHITECTURE.md) — options are bound via `IOptionsMonitor` and re-read every cycle, no restart needed).

## Host (`src/TacticalApi.Simulator.Host/appsettings.json`)

The Host runs only the simulated `Situation` gRPC service, store, and map UI - it has no data sources of its own.

```jsonc
"Simulator": {
  "ReporterId": "TacticalAPI-Simulator",
  "ExpirySweepInterval": "00:00:10",
  "MapUi": {
    "Enabled": true,                       // false hides /ui, /api/objects and /api/config (404)
    "RefreshInterval": "00:00:02",         // how often /ui polls /api/objects
    "DefaultCenterLatitude": 53.08,        // initial map view, before any objects load
    "DefaultCenterLongitude": 8.8,
    "DefaultZoom": 9
  },
  "Performance": {
    "SubscriberChannelCapacity": 4096,     // per-subscriber event buffer
    "SubscriberChannelFullMode": "DropOldest", // or "Wait" for backpressure
    "StreamBatchSize": 256,                // objects per streamed response
    "MaxReceiveMessageSizeMb": 16,
    "MaxSituationObjects": 100000          // memory guard (no DB!)
  }
}
```

Performance-relevant behavior is configuration, not code: channel sizes, overflow strategy (`DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers), batch sizes and object caps. The host additionally runs with Server GC.

## Each adapter (`src/TacticalApi.Simulator.Adapter.*/appsettings.json`)

Every `Adapter.*` executable's own `appsettings.json` has just two things: where it pushes updates to, and its one source's own settings.

```jsonc
"Simulator": {
  "Ingest": {
    "Address": "http://localhost:5100"  // where this adapter pushes updates - see below
  },
  "Sources": {
    "OpenSky": { /* only present in Adapter.OpenSky's appsettings.json - see Sources.OpenSky's README */ }
  }
}
```

`Ingest:Address` is the gRPC endpoint the adapter pushes updates to (see [Architecture](ARCHITECTURE.md) — each adapter is a real `Situation.SituationClient` gRPC client, not an in-process shortcut). It defaults to the Host's own native gRPC endpoint, so running the Host plus any adapter keeps working out of the box, but it's just a config value: point it at any other implementation of the TacticalAPI contract and that one adapter drives that instead, independently of the others. If the endpoint is unreachable, the adapter logs `IngestFailed`/retries each cycle rather than crashing.

Each source's own settings (intervals, symbol codes, bounding boxes, ...) are documented in its own project, not duplicated here:

- [`Sources.OpenSky/README.md`](../src/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker (`Adapter.OpenSky`)
- [`Sources.Synthetic/README.md`](../src/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and the all-object-types scenario (`Adapter.Synthetic`)
- [`Sources.Nws/README.md`](../src/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts (`Adapter.Nws`)
