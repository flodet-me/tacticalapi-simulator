# Configuration

Everything lives under `Simulator` in `appsettings.json` and reloads at runtime (see [Architecture](ARCHITECTURE.md) — options are bound via `IOptionsMonitor` and re-read every cycle, no restart needed):

```jsonc
"Simulator": {
  "ReporterId": "TacticalAPI-Simulator",
  "ExpirySweepInterval": "00:00:10",
  "Ingest": {
    "Address": "http://localhost:5100"  // where sources push updates - see below
  },
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
  },
  "Sources": {
    "SyntheticScenario":  { /* enabled by default — see Sources.Synthetic's README */ },
    "SyntheticAirTracks": { /* see Sources.Synthetic's README */ },
    "OpenSky":            { /* disabled by default — see Sources.OpenSky's README */ },
    "Nws":                { /* disabled by default — see Sources.Nws's README */ }
  }
}
```

Performance-relevant behavior is configuration, not code: channel sizes, overflow strategy (`DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers), batch sizes and object caps. The host additionally runs with Server GC.

`Ingest:Address` is the gRPC endpoint every simulation source pushes updates to (see [Architecture](ARCHITECTURE.md) — sources are real `Situation.SituationClient` gRPC clients, not an in-process shortcut). It defaults to the simulator's own native gRPC endpoint, so everything keeps working out of the box, but it's just a config value: point it at any other implementation of the TacticalAPI contract and the same sources drive that instead. If the endpoint is unreachable, affected sources log `IngestFailed`/retry each cycle rather than crashing.

Each source's own settings (intervals, symbol codes, bounding boxes, ...) are documented in its own project, not duplicated here:

- [`Sources.OpenSky/README.md`](../src/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker
- [`Sources.Synthetic/README.md`](../src/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and the all-object-types scenario
- [`Sources.Nws/README.md`](../src/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts
