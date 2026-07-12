# Configuration

Everything lives under `Simulator` in `appsettings.json` and reloads at runtime (see [Architecture](ARCHITECTURE.md) — options are bound via `IOptionsMonitor` and re-read every cycle, no restart needed):

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
    "SyntheticScenario":  { /* enabled by default — see Sources.Synthetic's README */ },
    "SyntheticAirTracks": { /* see Sources.Synthetic's README */ },
    "OpenSky":            { /* disabled by default — see Sources.OpenSky's README */ }
  }
}
```

Performance-relevant behavior is configuration, not code: channel sizes, overflow strategy (`DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers), batch sizes and object caps. The host additionally runs with Server GC.

Each source's own settings (intervals, symbol codes, bounding boxes, ...) are documented in its own project, not duplicated here:

- [`Sources.OpenSky/README.md`](../src/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker
- [`Sources.Synthetic/README.md`](../src/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture and the all-object-types scenario
