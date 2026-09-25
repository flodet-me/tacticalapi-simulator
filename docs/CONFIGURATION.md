# Configuration

Every executable has its own `appsettings.json`, bound via `IOptionsMonitor` and re-read every cycle — no restart needed ([Architecture](ARCHITECTURE.md)). If the file is missing at startup, `AppSettingsBootstrap` (Core) writes it out from the executable's embedded copy — exactly the file shown below — so you always get an editable file instead of a process running on in-memory defaults. Only the base file is generated; `appsettings.{Environment}.json` is an optional override layer.

## Host (`src/simulator/TacticalApi.Simulator.Host/appsettings.json`)

The three gRPC services, their stores, and the map UI. No data sources.

```jsonc
"Simulator": {
  "ReporterId": "TacticalAPI-Simulator",
  "ExpirySweepInterval": "00:00:10",
  "Control": {
    "Enabled": true                        // false hides /api/control/* (404)
  },
  "Faults": {
    "Enabled": false,                      // master switch - nothing below applies without it
    "Seed": null,                          // set to make a flaky run reproducible
    "Latency": "00:00:00",                 // added to every RPC
    "LatencyJitter": "00:00:00",           // extra delay drawn per call from [0, this]
    "RpcErrorProbability": 0.0,            // the call itself faults
    "RpcStatusCode": "Unavailable",        // ...with this status
    "ErrorHeaderProbability": 0.0,         // a write returns header.success = false
    "DropWriteProbability": 0.0,           // a write is acknowledged and discarded
    "StreamAbortAfter": null               // kill each subscription this long after it opens
  },
  "MapUi": {
    "Enabled": true,                       // false hides /ui, /api/objects and /api/config (404)
    "RefreshInterval": "00:00:02",         // how often /ui polls /api/objects
    "DefaultCenterLatitude": 53.08,        // initial map view, before any objects load
    "DefaultCenterLongitude": 8.8,
    "DefaultZoom": 9
  },
  "BlueForce": {
    "KeepAliveTimeout": "00:01:00",        // no keep-alive for this long -> implicitly deleted
    "SweepInterval": "00:00:05",           // how often timed-out blue forces are swept
    "OwnIdentity": null,                   // string identity flagged own_blue_force, or null for none
    "MaxBlueForces": 10000                 // memory guard, like MaxSituationObjects below
  },
  "OwnPose": {
    "PrimarySource": null,                 // which source GetPosition returns; null = most recent
    "PositionTimeout": "00:00:30",         // no fresh fix for this long -> is_invalid_or_expired
    "SweepInterval": "00:00:05"            // how often that flag is re-evaluated and announced
  },
  "Performance": {
    "SubscriberChannelCapacity": 4096,     // per-subscriber event buffer (all three streams)
    "SubscriberChannelFullMode": "DropOldest", // or "Wait" for backpressure
    "StreamBatchSize": 256,                // objects per streamed response
    "MaxReceiveMessageSizeMb": 16,
    "MaxSituationObjects": 100000          // memory guard (no DB!)
  }
}
```

Performance behavior is configuration, not code. `DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers. The Host runs with Server GC. What the knobs cost is visible on `/metrics` — notably `tacticalapi_subscriber_events_dropped_total`.

### `Simulator:Faults`

Every probability is an independent per-call draw in `[0, 1]`; `0` disables that fault, and nothing applies unless `Enabled` is set — a stray probability left in a config file can't quietly degrade a normal run. Being `IOptionsMonitor` options, faults switch on and off **while a client stays connected**: you watch it react instead of restarting into a differently-broken server.

```bash
# Make every write fail in a way that doesn't throw, without restarting the Host.
jq '.Simulator.Faults.Enabled = true | .Simulator.Faults.ErrorHeaderProbability = 1.0' \
  src/simulator/TacticalApi.Simulator.Host/appsettings.json > tmp && mv tmp \
  src/simulator/TacticalApi.Simulator.Host/appsettings.json
```

| Fault | Why it's the nasty one |
| --- | --- |
| `ErrorHeaderProbability` | The RPC succeeds, so nothing throws — the error lives in a field many clients never read. Reach for it first. |
| `DropWriteProbability` | Its quieter sibling: acknowledged, never applied, detectable only by reconciling afterwards. |

Every fault that fires counts on `tacticalapi_faults_injected_total` tagged by kind, so a confusing client-side failure traces back to the fault instead of being mistaken for a real bug. `Seed` makes a run reproducible.

### `Simulator:BlueForce`

No delete RPC exists: the contract says a client calls `AddOrUpdateBlueForces` "at least every 30s" and "deletion is done implicitly when a timeout defined by the application is reached". `KeepAliveTimeout` is that timeout.

The default is **60s, not 30s**, on purpose — a client honouring the contract to the letter would otherwise race the sweeper every cycle, and a simulator that punished the documented cadence is a trap, not a test target. Shorten it when the timeout is what you want to exercise:

```bash
# Watch a blue force disappear five seconds after its sender stops reporting.
Simulator__BlueForce__KeepAliveTimeout=00:00:05 Simulator__BlueForce__SweepInterval=00:00:01 \
  dotnet run --project src/simulator/TacticalApi.Simulator.Host
```

A timed-out blue force is announced once with `is_deleted = true`, then dropped — a client that missed it learns the same from the next `GetBlueForces`.

`OwnIdentity` picks which blue force comes back flagged `own_blue_force` (for the bundled patrol, `blueforce:patrol:leader`; null flags nothing). It is server-side because `UpdateBlueForce` has no such field — the contract makes "which one is me" a property of the answering system, not of the report.

### `Simulator:OwnPose`

| Setting | Behavior |
| --- | --- |
| `PrimarySource` | The one position `GetPosition`/`SubscribePositionChangedEvents` return — the contract's "one selected as primary position source by the application". Null = most recent reporter, so a single-sensor setup looks right with no configuration. A configured primary that never reported yields *no* position rather than substituting another sensor, which would hide exactly the misconfiguration a client is likeliest to hit. |
| `PositionTimeout` | The contract's own case: a fix "previously determined via GNSS" that stops being refreshed keeps its coordinates and gains `is_invalid_or_expired`, "because the user entered a building". |
| `SweepInterval` | Makes that flip reach a *subscriber* — who is not calling `GetPosition` and would otherwise sit on a fix that quietly stopped being true. |

### `Simulator:Control`

Gates `/api/control/*` ([README](../README.md#control-endpoints)). On by default, like the map UI: this simulator has no security features by design, so gating a control surface behind a flag would be theatre. Turn it off when the situation must only be driveable through the contract itself — a conformance run, or a demo nobody should reset from a browser tab.

## Each adapter (`src/adapter/TacticalApi.Simulator.Adapter.*/appsettings.json`)

Two things only: where it pushes, and its own source's settings directly under `Adapter` — no `Sources` nesting (that mattered only when every source shared one file, before the Host/adapter split). `Adapter` is deliberately a separate root from the Host's `Simulator`: an adapter isn't the simulator, it's a process that feeds one.

```jsonc
"Adapter": {
  "Ingest": {
    "Address": "http://localhost:5100"  // where this adapter pushes updates - see below
  },
  "Recording": {                        // available in EVERY adapter - see below
    "Enabled": false,
    "Path": "recordings/recording.jsonl"
  },
  "OpenSky": { /* only present in Adapter.OpenSky's appsettings.json - see Sources.OpenSky's README */ }
}
```

**`Ingest:Address`** — the gRPC endpoint this adapter pushes to, as a real gRPC client, not an in-process shortcut. One address covers all three services (the `BlueForceTracking`/`OwnPose` clients share the channel), so it stays the single setting that repoints a whole adapter at any other TacticalAPI implementation, independently of the others. Unreachable endpoint = `IngestFailed` logged and retried each cycle, not a crash.

**`Adapter:Recording`** — turns any adapter into a recorder: every batch its sources push is appended to a replayable `.jsonl` on the way out. It decorates the ingest client rather than being a source, so it captures whatever that adapter produces with no per-source support and nothing lost — what lands in the file is what went on the wire.

```bash
# An offline OpenSky replay, captured from a live run.
Adapter__Recording__Enabled=true Adapter__Recording__Path=recordings/opensky.jsonl \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky
```

The file is truncated on start, so each run is one self-contained recording. Unlike most options here it is read once at startup — swapping the sink under a half-written recording would produce two useless files instead of one good one. To record a situation you *don't* produce, use `Adapter:Recorder` instead ([`Sources.Replay`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md)).

Each source's own settings live with the source:

- [`Sources.OpenSky`](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky flight tracker (`Adapter.OpenSky`)
- [`Sources.Synthetic`](../src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) — air-track picture, scripted scenarios, blue force patrol feeding `BlueForceTracking` + `OwnPose` (`Adapter.Synthetic`)
- [`Sources.Nws`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts (`Adapter.Nws`)
- [`Sources.Replay`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) — recorder and replay player (`Adapter.Replay`); sections `Adapter:Recorder`, `Adapter:Replay`

## Logging (every executable)

Every log call goes through a source-generated `[LoggerMessage]` method with a named-parameter template (each project's `Logging/Log.cs`), never interpolation, so arguments reach every provider as distinct properties. Console by default; add a rolling file with `Logging:File` (a sibling of the standard `Logging:LogLevel`):

```jsonc
"Logging": {
  "LogLevel": { /* ... */ },
  "File": {
    "Enabled": false,     // opt-in; console alone is enough for local runs
    "Path": "logs/host-.jsonl"   // rolls daily: "host-.jsonl" -> "host-20260415.jsonl"
  }
}
```

| | |
| --- | --- |
| Format | Newline-delimited JSON (`Serilog.Formatting.Json.JsonFormatter`), one object per event, every `{NamedProperty}` its own field — queryable by field, not just grep-able. |
| Level | `Logging:LogLevel`, same as the console; no second level config to keep in sync. |
| Reload | The one setting that is **not** hot-reloadable — the provider is wired into the host builder before the DI container exists. Restart to toggle. |
| Path | Relative to the executable's own working directory (`RunWorkingDirectory`, i.e. the project directory, not where `dotnet run` was invoked). `logs/` is gitignored. |
