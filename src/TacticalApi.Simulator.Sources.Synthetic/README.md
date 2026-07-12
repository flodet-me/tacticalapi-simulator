# TacticalApi.Simulator.Sources.Synthetic

Two fully offline sources — no network, no external dependency — for demos and load tests: a circular air-track picture, and a scripted mini scenario that exercises every situation object type in the TacticalAPI contract.

## `SyntheticAirTrackSource`

Registered via `AddSyntheticSources` (`SyntheticServiceCollectionExtensions.cs`). Config section: `Simulator:Sources:SyntheticAirTracks`, bound to `SyntheticAirTrackOptions`.

### How it works

Simulates `TrackCount` aircraft flying circular orbits around a center point:

1. A seeded `Random` (`Seed`) picks, per track, a fixed phase angle, an orbit radius (`RadiusKm × [0.5, 1.5)`), and a direction (clockwise/counterclockwise) — deterministic across runs for the same seed.
2. Angular speed is derived from `SpeedMetersPerSecond / radius`, so faster tracks or smaller orbits sweep proportionally faster.
3. Position is projected from the angle using a simple spherical approximation (`EarthRadiusKm = 6371.0`); course is tangential to the orbit (perpendicular to the radius, in the direction of travel).
4. Track ids are `synthetic:air:{i:D3}`, named `SIM{i:D3}`; every cycle re-emits all `TrackCount` tracks via `TrackUpdateFactory.CreateSymbolUpdate`.

### Configuration (`SyntheticAirTrackOptions`)

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` (class default) | **`appsettings.json` ships this disabled** — `SyntheticScenario` is the source enabled out of the box, see below |
| `UpdateInterval` | `00:00:02` | range 100ms–1h |
| `TrackCount` | `12` | range 1–10,000 |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80` | Bremen, by default |
| `RadiusKm` | `60` | orbit radius, range 0.1–2000 |
| `SpeedMetersPerSecond` | `180` | range 1–3000 |
| `Seed` | `42` | deterministic picture for a given seed |
| `SymbolCode` | `SFAPMF---------` | friendly air, MIL-STD-2525C |
| `SymbolCatalog` | `Mil2525C` | |
| `TrackTimeToLive` | `00:00:30` | range 1s–1h |
| `ReporterId` | `SIM-SYNTH-AIR` | |

## `SyntheticScenarioSource`

Config section: `Simulator:Sources:SyntheticScenario`, bound to `SyntheticScenarioOptions`. **Enabled by default** — this is what populates the simulator out of the box.

### How it works

Emits a coherent mini scenario every `UpdateInterval`, covering all 11 `oneof` object types of the TacticalAPI v0 contract in one pass:

| Object type | What it represents |
|---|---|
| `OrganizationUnit` ×3 | A company HQ ("A Coy") with two subordinated platoons (ORBAT via `SubordinatedOrganizationUnitCollection`) |
| `Route` | A rectangular patrol route ("Route BRAVO") — a `Line` location with 4 corners laid out `ExtentKm` around the center |
| `Symbol` | A patrol vehicle interpolated along the route perimeter; lap progress driven by `PatrolLapDuration` |
| `ActionTask` | The patrol order, `ActionTaskStatus` transitioning `NotStarted → InProgress → Complete` with a live `CompletionRatio` tied to lap progress |
| `ActionEvent` | Random incidents (sniper attack, artillery, booby trap, ...) near the route; emitted with probability `EventProbability` per cycle, expire via `EventTimeToLive` |
| `TextDocument` | Periodic SITREP chat lines; emitted with probability `ChatProbability` per cycle |
| `NatoMessageDocument` | A static OWNSITREP MTF-formatted message |
| `PictureDocument` | A recon photo (tiny embedded 1×1 PNG — just enough to be a valid payload) |
| `VoiceMessageDocument` | A radio check (tiny embedded WAV) |
| `SketchDocument` | A planning sketch line between two waypoints |
| `OverlayDocument` | An overlay carrying two nested phase-line `Symbol` objects |

Everything goes through the same `UpdateSituationObject` batch path as every other source — indistinguishable from an external TacticalAPI client.

### Configuration (`SyntheticScenarioOptions`)

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` | |
| `UpdateInterval` | `00:00:05` | range 500ms–1h |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80` | scenario anchor point |
| `ExtentKm` | `20` | rough extent of the scenario, range 0.5–500 |
| `Seed` | `1337` | deterministic event generation |
| `EventProbability` | `0.25` | chance per cycle of a new `ActionEvent` |
| `EventTimeToLive` | `00:03:00` | range 10s–24h |
| `ChatProbability` | `0.4` | chance per cycle of a new SITREP `TextDocument` |
| `PatrolLapDuration` | `00:10:00` | full loop time for the patrol symbol/task, range 1min–24h |
| `ReporterId` | `SIM-SCENARIO` | |
