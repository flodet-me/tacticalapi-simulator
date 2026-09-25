# TacticalApi.Simulator.Sources.Nws

Live source: polls the free, keyless [US National Weather Service active alerts API](https://www.weather.gov/documentation/services-web-api) for one state. Unlike the track-only sources, **one alert produces up to three different situation object types** from a single feed — this is the reference example for a source that isn't a track feed.

## `NwsAlertSource`

Registered via `AddNwsSources`. Section `Adapter:Nws` → `NwsOptions`. **Disabled by default**, and US-only (the API has no international coverage).

### How it works

1. Every `PollInterval`: `GET {BaseAddress}alerts/active?area={Area}&status=actual`. `Area` is a two-letter state/territory code — the API filters by state/zone, not a bounding box. `status=actual` excludes NWS test/exercise broadcasts, which otherwise share the feed with real alerts.
2. Each GeoJSON feature is one alert: `properties.event`, `severity`, `headline`, `description`, `sent`/`expires`, and an optional `geometry` (a `Polygon` — many alerts, e.g. area-wide statements, have none).
3. **Every** alert → a `TextDocument` (`MessageCategory = Warning`; `MessagePrecedence` from CAP `severity`: Extreme→Flash, Severe→Immediate, Moderate→Priority, Minor/Unknown→Routine). The only object type guaranteed per alert.
4. **With a polygon**, two more: a `Symbol` at the ring's centroid (via `TrackUpdateFactory.CreateSymbolUpdate`, expiring at the alert's own `expires` rather than a rolling TTL) and a `SketchDocument` outlining the warning area as a closed `Line`.
5. Features with no `id` or `event` are skipped; `MaxAlertsPerPoll` caps processing (`0` = unlimited).

The centroid is a plain average of the ring's points — GeoJSON closes rings by repeating the first point, so that one is weighted double. A cheap approximation, not a true polygon centroid, which is fine for placing a demo marker.

### Configuration (`NwsOptions`)

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | opt-in; everything else runs without it |
| `BaseAddress` | `https://api.weather.gov/` | |
| `Area` | `OK` | two-letter US state/territory code |
| `PollInterval` | `00:02:00` | range 30s–1h |
| `MaxAlertsPerPoll` | `100` | `0` = unlimited |
| `SymbolCode` | `GHGPGPO---****X` | illustrative hazard marker — MIL-STD-2525C has no native weather symbology |
| `SymbolCatalog` | `Mil2525C` | |
| `TrackTimeToLive` | `00:15:00` | fallback only, for an alert with no `expires`; normally the alert's own is used |
| `ReporterId` | `SIM-NWS` | |

The last four are inherited from the shared `TrackEmitterOptions` base in `TacticalApi.Simulator.Sources` (as in `OpenSkyOptions`/`SyntheticAirTrackOptions`), even though this source also emits non-track types.

The API requires an identifying `User-Agent` (no API key), set once on the named `HttpClient` in `NwsServiceCollectionExtensions`, not per request. That client uses the same `AddStandardResilienceHandler` setup as [`Sources.OpenSky`](../TacticalApi.Simulator.Sources.OpenSky/README.md#resilience) — retry with backoff and jitter honoring `Retry-After`, a circuit breaker, and an infinite `HttpClient.Timeout` bounded by the handler's own.
