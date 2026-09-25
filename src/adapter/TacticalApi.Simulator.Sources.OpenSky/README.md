# TacticalApi.Simulator.Sources.OpenSky

Live source: polls the public [OpenSky Network](https://opensky-network.org/) REST API for real aircraft state vectors and maps every aircraft in a configurable bounding box onto a TacticalAPI `Symbol`. This is the reference implementation for plugging in any other online tracker (AIS, ADS-B, …) — see [Extending](../../../docs/EXTENDING.md).

## `OpenSkySource`

Registered via `AddOpenSkySources`. Section `Adapter:OpenSky` → `OpenSkyOptions`. **Disabled by default**, so the simulator runs fully offline out of the box.

### How it works

1. Every `PollInterval`: `GET {BaseAddress}states/all?lamin=…&lomin=…&lamax=…&lomax=…`.
2. Each row of `states` is a fixed-layout state vector, read by index: `icao24` (0), `callsign` (1), `origin_country` (2), `longitude` (5), `latitude` (6), `baro_altitude` (7), `on_ground` (8), `velocity` (9), `true_track` (10), `geo_altitude` (13).
3. Rows with no `icao24` or no position are skipped. Altitude prefers `geo_altitude`, falls back to `baro_altitude`, and is forced to `0` when `on_ground`.
4. Each aircraft → a `TrackReport` with id `opensky:{icao24}`, name = trimmed callsign (uppercased `icao24` when blank).
5. `TrackUpdateFactory.CreateSymbolUpdate` turns that into an `UpdateSituationObject` using the `SymbolCode` / `SymbolCatalog` / `ReporterId` / `TrackTimeToLive` options.
6. `MaxTracksPerPoll` bounds ingest cost regardless of how busy the box is (`0` = unlimited).
7. There is no "aircraft left the box" event — a track stops being reported and expires after `TrackTimeToLive`, then the store's sweeper marks it deleted.

### Resilience

The named `HttpClient` goes through [`AddStandardResilienceHandler`](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience) rather than a bare timeout: retry with exponential backoff and jitter — honoring `Retry-After`, so a `429` from OpenSky's rate limit backs off instead of hammering again next attempt — plus a circuit breaker, so a transient failure doesn't fail `ProduceAsync` outright every cycle. `HttpClient.Timeout` is left infinite; the handler's own per-attempt/total timeouts (10s/30s) bound each call.

### Configuration (`OpenSkyOptions`)

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | opt-in; everything else runs without it |
| `BaseAddress` | `https://opensky-network.org/api/` | |
| `PollInterval` | `00:00:15` | range 5s–1h; **anonymous access is rate-limited — keep ≥ 10s** |
| `MinLatitude` / `MaxLatitude` | `47.2` / `55.1` | bounding box, default roughly Germany |
| `MinLongitude` / `MaxLongitude` | `5.8` / `15.1` | |
| `MaxTracksPerPoll` | `500` | `0` = unlimited |
| `SymbolCode` | `SNAPCF---------` | neutral air, MIL-STD-2525C |
| `SymbolCatalog` | `Mil2525C` | |
| `TrackTimeToLive` | `00:02:00` | range 1s–1h |
| `ReporterId` | `SIM-OPENSKY` | |
