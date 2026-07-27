# TacticalApi.Simulator.Sources.OpenSky

Live data source: polls the public [OpenSky Network](https://opensky-network.org/) REST API for real aircraft state
vectors and maps every aircraft inside a configurable bounding box onto a TacticalAPI `Symbol` update. This is the
reference implementation for plugging any other online tracker (AIS ships, ADS-B, ...) into the simulator —
see [Extending the simulator](../../docs/EXTENDING.md).

## `OpenSkySource`

Registered via `AddOpenSkySources` (`OpenSkyServiceCollectionExtensions.cs`). Config section:
`Simulator:OpenSky`, bound to `OpenSkyOptions`. **Disabled by default** so the simulator runs fully offline out
of the box.

### How it works

1. Every `PollInterval`, `GET {BaseAddress}states/all?lamin=...&lomin=...&lamax=...&lomax=...` using the configured
   bounding box.
2. Each row of the `states` array is a fixed-layout OpenSky state vector; the source reads the fields it needs by index:
   `icao24` (0), `callsign` (1), `origin_country` (2), `longitude` (5), `latitude` (6), `baro_altitude` (7),
   `on_ground` (8), `velocity` (9), `true_track` (10), `geo_altitude` (13).
3. Rows missing an `icao24` or a position are skipped. Altitude prefers `geo_altitude`, falls back to `baro_altitude`,
   and is forced to `0` when `on_ground` is `true`.
4. Each aircraft becomes a `TrackReport` with id `opensky:{icao24}` and name = callsign (trimmed; falls back to the
   uppercased `icao24` if the callsign is blank).
5. `TrackUpdateFactory.CreateSymbolUpdate` turns the `TrackReport` into an `UpdateSituationObject` using `SymbolCode` /
   `SymbolCatalog` / `ReporterId` / `TrackTimeToLive` from options.
6. `MaxTracksPerPoll` caps how many rows are processed per poll (`0` = unlimited), bounding ingest cost regardless of
   how many aircraft are in the box.
7. There's no explicit "aircraft left the box" event — a track simply stops being reported and expires on its own once
   `TrackTimeToLive` elapses (the store's background sweeper then marks it deleted).

### Configuration (`OpenSkyOptions`)

| Setting                         | Default                            | Notes                                                                       |
|---------------------------------|------------------------------------|-----------------------------------------------------------------------------|
| `Enabled`                       | `false`                            | opt-in; everything else in the simulator runs without it                    |
| `BaseAddress`                   | `https://opensky-network.org/api/` |                                                                             |
| `PollInterval`                  | `00:00:15`                         | range 5s–1h; **anonymous OpenSky access is rate-limited — keep this ≥ 10s** |
| `MinLatitude` / `MaxLatitude`   | `47.2` / `55.1`                    | bounding box, default roughly Germany                                       |
| `MinLongitude` / `MaxLongitude` | `5.8` / `15.1`                     |                                                                             |
| `MaxTracksPerPoll`              | `500`                              | `0` = unlimited                                                             |
| `SymbolCode`                    | `SNAPCF---------`                  | neutral air, MIL-STD-2525C                                                  |
| `SymbolCatalog`                 | `Mil2525C`                         |                                                                             |
| `TrackTimeToLive`               | `00:02:00`                         | range 1s–1h                                                                 |
| `ReporterId`                    | `SIM-OPENSKY`                      |                                                                             |
