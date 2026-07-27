# TacticalApi.Simulator.Sources.Nws

Live data source: polls the free,
keyless [US National Weather Service active alerts API](https://www.weather.gov/documentation/services-web-api) for one
state. Unlike the track-only sources (`Sources.OpenSky`, `Sources.Synthetic`'s `SyntheticAirTrackSource`), a single
alert here produces up to **three different situation object types** from one feed — this is the reference example for a
source that isn't just a track feed.

## `NwsAlertSource`

Registered via `AddNwsSources` (`NwsServiceCollectionExtensions.cs`). Config section: `Adapter:Nws`, bound to
`NwsOptions`. **Disabled by default**, and US-only (the NWS API has no international coverage).

### How it works

1. Every `PollInterval`, `GET {BaseAddress}alerts/active?area={Area}&status=actual` — `Area` is a two-letter US
   state/territory code (the NWS API filters by state/zone, not a bounding box). `status=actual` excludes NWS
   test/exercise broadcasts, which otherwise appear in the same feed as real alerts.
2. Each GeoJSON feature in the response is one active alert: `properties.event` (e.g. "Flood Warning"), `severity` (
   Extreme/Severe/Moderate/Minor/Unknown), `headline`, `description`, `sent`/`expires` timestamps, and an optional
   `geometry` (a `Polygon` — many alerts, e.g. area-wide statements, have none).
3. **Every** alert becomes a `TextDocument` (`MessageCategory = Warning`; `MessagePrecedence` mapped from CAP
   `severity`: Extreme→Flash, Severe→Immediate, Moderate→Priority, Minor/Unknown→Routine) — this is the only object type
   guaranteed per alert.
4. **When the alert has a polygon**, two more objects are added: a `Symbol` marker at the ring's centroid (via
   `TrackUpdateFactory.CreateSymbolUpdate`, expiring at the alert's own `expires` time rather than a rolling TTL) and a
   `SketchDocument` outlining the warning area as a closed `Line`.
5. Rows missing an `id` or `event` are skipped. `MaxAlertsPerPoll` caps how many features are processed per poll (`0` =
   unlimited).

The centroid is a plain average of the ring's points (GeoJSON closes rings by repeating the first point, so that point
is weighted double) — a cheap approximation, not a true polygon centroid, which is fine for placing a demo marker.

### Configuration (`NwsOptions`)

| Setting            | Default                    | Notes                                                                                                                     |
|--------------------|----------------------------|---------------------------------------------------------------------------------------------------------------------------|
| `Enabled`          | `false`                    | opt-in; everything else in the simulator runs without it                                                                  |
| `BaseAddress`      | `https://api.weather.gov/` |                                                                                                                           |
| `Area`             | `OK`                       | two-letter US state/territory code                                                                                        |
| `PollInterval`     | `00:02:00`                 | range 30s–1h                                                                                                              |
| `MaxAlertsPerPoll` | `100`                      | `0` = unlimited                                                                                                           |
| `SymbolCode`       | `GHGPGPO---****X`          | illustrative hazard marker — MIL-STD-2525C has no native weather symbology                                                |
| `SymbolCatalog`    | `Mil2525C`                 |                                                                                                                           |
| `TrackTimeToLive`  | `00:15:00`                 | fallback only, used when an alert somehow has no `expires` timestamp; normally the alert's own `expires` is used directly |
| `ReporterId`       | `SIM-NWS`                  |                                                                                                                           |

`SymbolCode`, `SymbolCatalog`, `TrackTimeToLive`, and `ReporterId` are inherited from the shared `TrackEmitterOptions`
base in `TacticalApi.Simulator.Sources` (same as `OpenSkyOptions`/`SyntheticAirTrackOptions`), even though this source
also emits non-track object types.

The NWS API requires an identifying `User-Agent` header (no API key) — set once on the named `HttpClient` in
`NwsServiceCollectionExtensions`, not per-request.
