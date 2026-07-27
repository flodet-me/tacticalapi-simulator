# TacticalApi.Simulator.Sources.Synthetic

Four fully offline sources — no network, no external dependency — for demos and load tests: a circular air-track
picture, a scripted mini scenario that exercises every situation object type in the TacticalAPI contract, and two
scenarios modeling real military operations end to end — a convoy escort and a combat outpost defense — with
friendly/hostile forces and engagements resolved probabilistically rather than scripted.

`GeoMath.cs` (destination-point projection, haversine distance, point-in-polygon containment) and
`LanchesterModel.cs` (Lanchester's Square Law attrition) are shared by every scenario below that needs real-world
geometry or engagement resolution.

## `SyntheticAirTrackSource`

Registered via `AddSyntheticSources` (`SyntheticServiceCollectionExtensions.cs`). Config section:
`Adapter:SyntheticAirTracks`, bound to `SyntheticAirTrackOptions`.

### How it works

Simulates `TrackCount` aircraft flying circular orbits around a center point:

1. A seeded `Random` (`Seed`) picks, per track, a fixed phase angle, an orbit radius (`RadiusKm × [0.5, 1.5)`), and a
   direction (clockwise/counterclockwise) — deterministic across runs for the same seed.
2. Angular speed is derived from `SpeedMetersPerSecond / radius`, so faster tracks or smaller orbits sweep
   proportionally faster.
3. Position is projected from the angle using a simple spherical approximation (`EarthRadiusKm = 6371.0`); course is
   tangential to the orbit (perpendicular to the radius, in the direction of travel).
4. Track ids are `synthetic:air:{i:D3}`, named `SIM{i:D3}`; every cycle re-emits all `TrackCount` tracks via
   `TrackUpdateFactory.CreateSymbolUpdate`.

### Configuration (`SyntheticAirTrackOptions`)

| Setting                              | Default                | Notes                                                                                                            |
|--------------------------------------|------------------------|------------------------------------------------------------------------------------------------------------------|
| `Enabled`                            | `true` (class default) | **`appsettings.json` ships this disabled** — `SyntheticScenario` is the source enabled out of the box, see below |
| `UpdateInterval`                     | `00:00:02`             | range 100ms–1h                                                                                                   |
| `TrackCount`                         | `12`                   | range 1–10,000                                                                                                   |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80`       | Bremen, by default                                                                                               |
| `RadiusKm`                           | `60`                   | orbit radius, range 0.1–2000                                                                                     |
| `SpeedMetersPerSecond`               | `180`                  | range 1–3000                                                                                                     |
| `Seed`                               | `42`                   | deterministic picture for a given seed                                                                           |
| `SymbolCode`                         | `SFAPMF---------`      | friendly air, MIL-STD-2525C                                                                                      |
| `SymbolCatalog`                      | `Mil2525C`             |                                                                                                                  |
| `TrackTimeToLive`                    | `00:00:30`             | range 1s–1h                                                                                                      |
| `ReporterId`                         | `SIM-SYNTH-AIR`        |                                                                                                                  |

## `SyntheticScenarioSource`

Config section: `Adapter:SyntheticScenario`, bound to `SyntheticScenarioOptions`. **Enabled by default** —
this is what populates the simulator out of the box.

### How it works

Emits a coherent mini scenario every `UpdateInterval`, covering all 11 `oneof` object types - and most of the
contract's location kinds - of the TacticalAPI v0 contract in one pass:

| Object type            | What it represents                                                                                                                                                    |
|------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `OrganizationUnit` ×3  | A company HQ ("A Coy") with two subordinated platoons (ORBAT via `SubordinatedOrganizationUnitCollection`)                                                          |
| `Route` ×2             | "Route BRAVO", an irregular six-checkpoint patrol loop as a `RouteLocation` (named/commented waypoints with ETAs); "Route CHARLIE", a resupply air corridor as a `Corridor` (500 m wide) |
| `Symbol`               | A patrol vehicle interpolated along Route BRAVO's perimeter (lap progress driven by `PatrolLapDuration`); "Objective HOTEL", a `Polygon` assembly area with a demo `ForeignKey` |
| `ActionTask`           | The patrol order, `ActionTaskStatus` transitioning `NotStarted → InProgress → Complete` with a live `CompletionRatio` tied to lap progress                          |
| `ActionEvent`          | Random incidents near the route, each with a location kind matching its nature: sniper attack (`Fan` detection arc), artillery fire (`Ellipse` impact area), booby-trap belt (`Multipoint` device cluster), or a plain point (traffic accident, acoustic fix); emitted with probability `EventProbability` per cycle, expire via `EventTimeToLive` |
| `TextDocument`         | Periodic SITREP chat lines; emitted with probability `ChatProbability` per cycle                                                                                    |
| `NatoMessageDocument`  | A static OWNSITREP MTF-formatted message                                                                                                                            |
| `PictureDocument`      | A recon photo (tiny embedded 1×1 PNG — just enough to be a valid payload)                                                                                           |
| `VoiceMessageDocument` | A radio check (tiny embedded WAV)                                                                                                                                   |
| `SketchDocument`       | A multi-element planning sketch (`SketchLocation`): the objective outline (dashed polygon), the axis of advance (solid line), and a rally point marker - each individually colored/styled |
| `OverlayDocument`      | An overlay carrying two nested phase-line `Symbol` objects                                                                                                          |

Everything goes through the same `UpdateSituationObject` batch path as every other source — indistinguishable from an
external TacticalAPI client. The patrol loop, objective area, and forward operating base are laid out once at startup
from a small deterministic geometry builder (seeded by `Seed`), so the shapes stay stable across cycles even though
the patrol symbol and incidents move/appear every cycle.

### Configuration (`SyntheticScenarioOptions`)

| Setting                              | Default          | Notes                                                     |
|--------------------------------------|------------------|-----------------------------------------------------------|
| `Enabled`                            | `true`           |                                                           |
| `UpdateInterval`                     | `00:00:05`       | range 500ms–1h                                            |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80` | scenario anchor point                                     |
| `ExtentKm`                           | `20`             | rough extent of the scenario, range 0.5–500               |
| `Seed`                               | `1337`           | deterministic event generation                            |
| `EventProbability`                   | `0.25`           | chance per cycle of a new `ActionEvent`                   |
| `EventTimeToLive`                    | `00:03:00`       | range 10s–24h                                             |
| `ChatProbability`                    | `0.4`            | chance per cycle of a new SITREP `TextDocument`           |
| `PatrolLapDuration`                  | `00:10:00`       | full loop time for the patrol symbol/task, range 1min–24h |
| `ReporterId`                         | `SIM-SCENARIO`   |                                                           |

## `ConvoyEscortSource`

Config section: `Adapter:ConvoyEscort`, bound to `ConvoyEscortOptions`. **Disabled by default** — opt-in
alongside the base scenario.

### How it works

A logistics convoy (callsign `TRIREME`: a lead and a trail gun truck escorting `CargoVehicleCount` trucks) shuttles
back and forth along `Route CONDOR` between `StartLatitude/Longitude` and `EndLatitude/Longitude`, turning around
every `TransitDuration` — replacement personnel are assumed between runs, so casualties don't accumulate forever.

1. Three scripted high-risk zones sit at fixed fractions along the route (a culvert, a market chokepoint, a rail
   underpass), each named in the route's `RouteLocation` waypoints. Ambush probability per cycle is
   `BaseAmbushProbability`, multiplied by `RiskZoneMultiplier` while the lead vehicle is within `RiskZoneRadiusM` of
   one — real ambush risk is not uniform along a route, it clusters at terrain choke points.
2. On a triggered ambush (subject to `ContactCooldown`), a coin flip weighted by `IedProbabilityGivenContact` decides
   IED-initiated (an `Ellipse` blast area) vs. pure small-arms contact (a `Fan` engagement arc from a nearby stand-off
   position). A hostile element (3-8 dismounted fighters, MIL-STD-2525C hostile affiliation) is spawned and the
   engagement is resolved with `LanchesterModel` — IED contact gives the ambusher a surprise-driven effectiveness
   edge; pure small-arms contact favors the escort's training/firepower.
3. Friendly casualties reduce that vehicle's carried personnel (reflected in its `AdditionalInformation`); if any
   casualties resulted, a `CASEVAC request` `ActionTask` (`Priority1`) is raised at the contact point.
4. Every cycle also refreshes a persistent `NatoMessageDocument` carrying the latest contact as a SALUTE-format report
   (Size/Activity/Location/Unit/Time/Equipment) — a real US military spot-report format.

### Configuration (`ConvoyEscortOptions`)

| Setting                              | Default        | Notes                                                  |
|---------------------------------------|----------------|--------------------------------------------------------|
| `Enabled`                             | `false`        |                                                         |
| `UpdateInterval`                      | `00:00:05`     | range 500ms–1h                                          |
| `StartLatitude`/`StartLongitude`      | `52.92`/`8.55` | one end of the route                                    |
| `EndLatitude`/`EndLongitude`          | `53.20`/`8.60` | the other end                                           |
| `TransitDuration`                     | `00:20:00`     | one-way leg time, range 5min–24h                        |
| `CargoVehicleCount`                   | `4`            | range 1–20                                              |
| `SecurityVehicleCount`                | `2`            | range 1–8 (split lead/trail)                            |
| `PersonnelPerVehicle`                 | `4`            | range 1–50                                              |
| `BaseAmbushProbability`               | `0.01`         | per cycle, away from any risk zone                      |
| `RiskZoneMultiplier`                  | `20.0`         | applied within a risk zone, range 1–200                 |
| `RiskZoneRadiusM`                     | `300`          | range 50–5000                                           |
| `IedProbabilityGivenContact`          | `0.5`          | chance an ambush is IED- vs. small-arms-initiated        |
| `ContactCooldown`                     | `00:03:00`     | minimum time between contacts, range 30s–1h             |
| `Seed`                                | `2024`         | deterministic risk-zone layout and contact rolls        |
| `ReporterId`                          | `SIM-CONVOY`   |                                                         |

## `CombatOutpostDefenseSource`

Config section: `Adapter:CombatOutpostDefense`, bound to `CombatOutpostDefenseOptions`. **Disabled by
default** — opt-in alongside the base scenario.

### How it works

A static combat outpost ("COP RESOLUTE": an octagonal defended perimeter with `ObservationPostCount` OPs around it)
is probed by a persistent local hostile cell. Two things make this a simulation rather than scripted flavor:

1. **The clock matters.** Contact probability is `DayContactProbability`, multiplied by
   `NightContactProbabilityMultiplier` between `NightStartHourUtc` and `NightEndHourUtc` (checked against the actual
   UTC hour) — real irregular/insurgent activity skews heavily toward darkness, and this reproduces that skew
   directly instead of a flat random rate. The standing `Defend COP RESOLUTE` `ActionTask`'s priority (`Priority1` at
   night, `Priority3` by day) reflects the same posture change.
2. **Both sides have a memory.** Hostile cell strength and garrison strength are persistent pools (starting at
   `InitialHostileCellStrength`/`GarrisonStrength`) that deplete with casualties and slowly reconstitute
   (`HostileReinforcementPerHour`/`GarrisonReplacementPerHour`) — no infinite respawn, no instant recovery. Both
   figures are visible in the `ActionTask`'s `AdditionalInformation`.

Given a contact (subject to `ContactCooldown`), one of three real outcomes is rolled:

- **Indirect fire** (`ArtilleryFire`, probability `IndirectFireProbabilityGivenContact`): a mortar-style `Ellipse`
  impact lands at a random bearing/range from the center. `GeoMath.Contains` checks the impact against the actual
  perimeter polygon — inside the wire risks a handful of casualties, outside the wire is a near-miss with none. This
  is a real technique (impact-vs-perimeter containment), not a coin flip.
- **Ground assault** (`Ambush`, probability `AssaultProbabilityGivenContact`): a larger hostile element (40-70% of
  the cell) advances along a `Fan` axis; half the garrison stands-to. `LanchesterModel` resolves the engagement, and
  3+ friendly casualties raise a `QRF reinforcement` `ActionTask`.
- **Harassing/sniper fire** (`SniperAttack`, the remaining probability): a small element fires from stand-off (a
  `Fan` arc) against the nearest OP's element; resolved the same way, defender-favored.

A persistent `NatoMessageDocument` SITREP (posture + latest contact) refreshes every cycle.

### Configuration (`CombatOutpostDefenseOptions`)

| Setting                                | Default   | Notes                                                        |
|------------------------------------------|-----------|----------------------------------------------------------------|
| `Enabled`                                | `false`   |                                                                  |
| `UpdateInterval`                         | `00:00:10`| range 500ms–1h                                                  |
| `CenterLatitude`/`CenterLongitude`       | `53.00`/`9.05` | perimeter center                                           |
| `PerimeterRadiusM`                       | `250`     | range 50–2000                                                   |
| `ObservationPostCount`                   | `4`       | range 1–12                                                      |
| `GarrisonStrength`                       | `40`      | starting/max personnel, range 1–500                             |
| `InitialHostileCellStrength`             | `25`      | starting/max hostile strength, range 1–500                      |
| `DayContactProbability`                  | `0.02`    | baseline, per cycle                                              |
| `NightContactProbabilityMultiplier`      | `5.0`     | range 1–50                                                       |
| `NightStartHourUtc`/`NightEndHourUtc`    | `19`/`6`  | UTC hours, wraps past midnight                                   |
| `AssaultProbabilityGivenContact`         | `0.08`    | given a contact                                                  |
| `IndirectFireProbabilityGivenContact`    | `0.4`     | given a non-assault contact                                      |
| `HostileReinforcementPerHour`            | `0.6`     | strength regenerated per hour, range 0–50                        |
| `GarrisonReplacementPerHour`             | `0.3`     | personnel replaced per hour, range 0–50                          |
| `ContactCooldown`                        | `00:08:00`| minimum time between contacts, range 30s–4h                      |
| `Seed`                                   | `4077`    | deterministic perimeter/OP layout and contact rolls              |
| `ReporterId`                             | `SIM-COP` |                                                                  |
