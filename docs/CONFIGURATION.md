# Configuration

Every executable has its own `appsettings.json` and reloads at runtime (see [Architecture](ARCHITECTURE.md) — options are bound via `IOptionsMonitor` and re-read every cycle, no restart needed).

If an executable's `appsettings.json` is missing next to it at startup (e.g. a bare copy of just the `.dll`, or a fresh volume mount), it writes one out from its own embedded copy — the exact file shown below for that executable — before loading configuration, so you always get a discoverable, editable file rather than a process silently running on in-memory defaults. See `AppSettingsBootstrap` in `TacticalApi.Simulator.Core`. This only applies to the base `appsettings.json`; `appsettings.{Environment}.json` is an optional override layer and is never generated.

## Host (`src/simulator/TacticalApi.Simulator.Host/appsettings.json`)

The Host runs the simulated gRPC services of the contract - `Situation`, `BlueForceTracking` and `OwnPose` - their stores, and the map UI. It has no data sources of its own.

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

Performance-relevant behavior is configuration, not code: channel sizes, overflow strategy (`DropOldest` keeps streams fresh for state-based tracks; `Wait` applies backpressure to producers), batch sizes and object caps. The host additionally runs with Server GC. What those knobs are actually doing is visible on `/metrics` — in particular `tacticalapi_subscriber_events_dropped_total`, which is what `DropOldest` costs you.

### `Simulator:Faults`

Makes the simulator misbehave on purpose, so a client can be proven to survive a server that is slow, flaky, rejecting writes, or dropping long-lived streams. Every probability is an independent per-call draw in `[0, 1]`; `0` disables that fault, and nothing applies at all unless `Enabled` is set — a stray probability left in a config file can't quietly degrade a normal run.

Because these are `IOptionsMonitor` options like everything else, faults can be switched on and off **while a client stays connected**, which is the point: you watch it react instead of restarting into a differently-broken server.

```bash
# Make every write fail in a way that doesn't throw, without restarting the Host.
jq '.Simulator.Faults.Enabled = true | .Simulator.Faults.ErrorHeaderProbability = 1.0' \
  src/simulator/TacticalApi.Simulator.Host/appsettings.json > tmp && mv tmp \
  src/simulator/TacticalApi.Simulator.Host/appsettings.json
```

`ErrorHeaderProbability` is the one worth reaching for first: the RPC succeeds, so nothing throws, and the error exists only in a field many clients never read. `DropWriteProbability` is its quieter sibling — acknowledged, never applied, detectable only by reconciling afterwards. Every fault that fires is counted on `tacticalapi_faults_injected_total` tagged by kind, so a confusing client-side failure can be traced back to the fault that caused it rather than mistaken for a real bug.

Set `Seed` to make a run reproducible; leave it null for a different sequence each time.

### `Simulator:BlueForce`

`BlueForceTracking` has no delete RPC: the contract says a client must call `AddOrUpdateBlueForces` "at least every 30s" and that "deletion is done implicitly when a timeout defined by the application is reached". `KeepAliveTimeout` is that timeout.

The default is **60s, not 30s**, on purpose. A client honouring the contract to the letter — calling exactly every 30s — would otherwise be racing the sweeper on every single cycle, and a simulator that punished the documented cadence would be a trap rather than a test target. Shorten it deliberately when the timeout is what you want to exercise:

```bash
# Watch a blue force disappear five seconds after its sender stops reporting.
Simulator__BlueForce__KeepAliveTimeout=00:00:05 Simulator__BlueForce__SweepInterval=00:00:01 \
  dotnet run --project src/simulator/TacticalApi.Simulator.Host
```

A timed-out blue force is announced once on `SubscribeBlueForceEvents` with `is_deleted = true` and then dropped, so a client that missed the announcement learns the same thing from its absence in the next `GetBlueForces`.

`OwnIdentity` decides which blue force comes back flagged `own_blue_force`. It is server-side rather than taken from the update because `UpdateBlueForce` has no such field — the contract makes "which one is me" a property of the system answering, not of the report. Set it to the string identity of the blue force that should be flagged (for the bundled patrol scenario, `blueforce:patrol:leader`); leave it null and nothing is flagged.

### `Simulator:OwnPose`

`GetPosition` and `SubscribePositionChangedEvents` return exactly one position — "the one selected as primary position source by the application". `PrimarySource` is that selection. Left null, whichever source reported most recently wins, which is what makes a single-sensor setup look right with no configuration at all. Set it to a source identifier to pin the choice; a configured primary that has never reported yields *no* position rather than quietly substituting another sensor, since silently answering with a different source would hide exactly the misconfiguration a client is likeliest to hit.

`PositionTimeout` models the case the contract spells out: a fix "previously determined via GNSS" that stops being refreshed keeps its coordinates and gains `is_invalid_or_expired`, "because the user entered a building". The staleness sweeper exists so that flip reaches a *subscriber* — who by definition is not calling `GetPosition` and would otherwise sit on a fix that quietly stopped being true.

### `Simulator:Control`

Gates `/api/control/*` (see [README](../README.md#control-endpoints)). On by default, consistent with the map UI: this simulator has no security features at all by design, so gating a control surface behind a flag would be security theatre rather than security. Turn it off when the situation must only be driveable through the TacticalAPI contract itself — a conformance run, or a demo nobody should be able to reset from a browser tab.

## Each adapter (`src/adapter/TacticalApi.Simulator.Adapter.*/appsettings.json`)

Every `Adapter.*` executable's own `appsettings.json` has just two things: where it pushes updates to, and its own source's settings sitting directly under `Adapter` (no extra `Sources` nesting - that only ever mattered when every source's config lived together in one shared file, before the Host/adapter split; now each file already belongs to one adapter, so the source's own name is enough). `Adapter` is deliberately a separate root from the Host's own `Simulator` section above - an adapter isn't the simulator, it's a process that feeds one.

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

### `Adapter:Recording` (every adapter)

Turns any adapter into a recorder: with this on, every batch its sources push is also appended to a replayable `.jsonl` recording on the way out. It decorates the ingest client rather than being a source of its own, so it captures whatever that adapter produces — OpenSky, NWS, a scenario — with no per-source support needed and nothing lost: what lands in the file is exactly what went on the wire.

```bash
# An offline OpenSky replay, captured from a live run.
Adapter__Recording__Enabled=true Adapter__Recording__Path=recordings/opensky.jsonl \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.OpenSky
```

The file is truncated on start, so each run produces one self-contained recording. Unlike most options here this one is read once at startup — swapping the sink under a half-written recording would produce two useless files instead of one good one. To record a situation you *don't* produce (a third-party implementation, or one fed by clients you don't control), use `Adapter:Recorder` instead — see [`Sources.Replay`'s README](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) for which to reach for.

`Ingest:Address` is the gRPC endpoint the adapter pushes updates to (see [Architecture](ARCHITECTURE.md) — each adapter is a real gRPC client, not an in-process shortcut). One address covers all three services: an adapter feeding blue forces or an own position builds its `BlueForceTracking`/`OwnPose` client on the same channel, so this stays the single setting that repoints a whole adapter. It defaults to the Host's own native gRPC endpoint, so running the Host plus any adapter keeps working out of the box, but it's just a config value: point it at any other implementation of the TacticalAPI contract and that one adapter drives that instead, independently of the others. If the endpoint is unreachable, the adapter logs `IngestFailed`/retries each cycle rather than crashing.

Each source's own settings (intervals, symbol codes, bounding boxes, ...) are documented in its own project, not duplicated here:

- [`Sources.OpenSky/README.md`](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) — live OpenSky Network flight tracker (`Adapter.OpenSky`)
- [`Sources.Synthetic/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Synthetic/README.md) — offline air-track picture, the all-object-types scenario, and the blue force patrol that feeds `BlueForceTracking` + `OwnPose` (`Adapter.Synthetic`)
- [`Sources.Nws/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) — live US National Weather Service alerts (`Adapter.Nws`)
- [`Sources.Replay/README.md`](../src/adapter/TacticalApi.Simulator.Sources.Replay/README.md) — the situation recorder and the replay player (`Adapter.Replay`); config sections `Adapter:Recorder` and `Adapter:Replay`

## Logging (every executable)

Every executable already logs structured events - every log call across the codebase goes through a source-generated `[LoggerMessage]` method with a named-parameter template (see each project's own `Logging/Log.cs`), not string interpolation, so arguments always reach every provider as distinct properties rather than text baked into one string. By default that only goes to the console.

To also write it to a rolling file, set `Logging:File:Enabled` (a sibling of the standard `Logging:LogLevel` section, common to every `appsettings.json`):

```jsonc
"Logging": {
  "LogLevel": { /* ... */ },
  "File": {
    "Enabled": false,     // opt-in; console alone is enough for local runs
    "Path": "logs/host-.jsonl"   // rolls daily: "host-.jsonl" -> "host-20260415.jsonl"
  }
}
```

Written as newline-delimited JSON (`Serilog.Formatting.Json.JsonFormatter`), one object per log event with every `{NamedProperty}` from the call site kept as its own field - not flattened into the message text - so the file stays queryable by field (`jq`, a log aggregator, ...) instead of only grep-able by substring. `Logging:LogLevel` still controls the minimum level for this sink too, the same as it does for the console - there's no separate level config to keep in sync. Unlike the rest of this simulator's `IOptionsMonitor`-based config, this one setting isn't hot-reloadable: the log provider is wired into the host builder before the DI container exists, so toggling it requires a restart. Path is relative to the executable's own working directory (`RunWorkingDirectory` in `Directory.Build.props`, see [Architecture](ARCHITECTURE.md) - that's the project directory itself, not wherever `dotnet run` was invoked from), and `logs/` is gitignored.
