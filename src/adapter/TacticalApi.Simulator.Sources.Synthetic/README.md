# TacticalApi.Simulator.Sources.Synthetic

Eight fully offline sources — no network, no external dependency — for demos and load tests: a circular air-track
picture, a scripted mini scenario that exercises every situation object type in the TacticalAPI contract, two
scenarios modeling real military operations end to end — a convoy escort and a combat outpost defense — with
friendly/hostile forces and engagements resolved probabilistically rather than scripted, a blue force patrol that
feeds the contract's other two services (`BlueForceTracking` and `OwnPose`), a load generator that makes no
attempt at plausibility and simply moves as many objects as you ask for, a geometry showcase that holds one
static object of every location kind for checking a client's rendering, and a theater-scale demo picture of
NATO's eastern flank meant to be shown to someone rather than tested against.

**Which service a thing goes on** is decided the same way in every scenario here, and it is worth stating once:
a friendly element that reports its own position is a **blue force**; anything reported *about* — an enemy
contact, a graphic someone drew, an order, a message — is a **situation object**. So the convoy's gun trucks and
the combat outpost's observation posts go over `BlueForceTracking`, while their routes, perimeters, ambushes,
hostiles, tasks and reports stay on `Situation`. Emitting a friendly vehicle on both would put it into a client's
picture twice, from two services that disagree about what it is.

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

### What goes where

| Over `BlueForceTracking` | Over `Situation` |
| --- | --- |
| `convoy:vehicle:{i}` — every gun truck and cargo truck, spaced along the route, `is_vehicle` (the lead gun truck also `is_leader`) | `convoy:route:condor` (the supply route with its risk-zone waypoints) |
| | The ambush `ActionEvent`, the hostile symbols it spawns |
| | The CASEVAC `ActionTask`, the SALUTE `NatoMessageDocument` |

`BlueForce` has no free-text field, so a vehicle's state rides in its callsign —
`TRIREME LOGPAC 2 [2 WIA]`, or `[COMBAT INEFFECTIVE]` once it has no one left aboard, in which case it also
reports a speed of zero. The full casualty accounting still goes up in the SALUTE report. Point
`Simulator:BlueForce:OwnIdentity` at `convoy:vehicle:0` (the lead gun truck, where the convoy commander rides)
to see an own force.

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

### What goes where

| Over `BlueForceTracking` | Over `Situation` |
| --- | --- |
| `cop:bf:cp` — the command post, `is_leader`, with the garrison's effective strength in its callsign | `cop:perimeter` (the defended perimeter polygon — a graphic the commander drew, not a force) |
| `cop:bf:op:{i}` — every manned observation post | `cop:task:defend` and `cop:task:qrf`, the hostile elements, the SITREP |

Point `Simulator:BlueForce:OwnIdentity` at `cop:bf:cp` to see an own force.

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

## `BlueForcePatrolSource`

Config section: `Adapter:BlueForcePatrol`, bound to `BlueForcePatrolOptions`. **Enabled by default** — it is the
only source that puts anything behind `BlueForceTracking` and `OwnPose`, so running the Host plus this adapter
should show all three services of the contract alive without further configuration.

The convoy and combat outpost scenarios feed `BlueForceTracking` too (see above). This one is still the source
to reach for when the blue force contract itself is what you are testing: it is the only one that exercises
`mount_host`, an unmanned blue force, and `OwnPose` — including a position source that drops out.

This one is a blue force source, not a simulation source, and that distinction is the point. A blue force is not a
situation object with a friendly affiliation: it is a friendly participant updating its *own* position on a
keep-alive cadence, and the contract gives it its own service, its own message and its own implicit deletion rule.
Emitting these as `Symbol`s as well would put the same vehicle into a client's picture twice, from two services
that disagree about what it is — so the scenarios above own the situation picture and this one owns the blue force
picture.

### How it works

A section ("BADGER") patrols a loop around `CenterLatitude`/`CenterLongitude`:

- **A carrier vehicle** (`is_vehicle`) driving the loop, one lap per `LapDuration`.
- **A section leader** (`is_leader`) and `DismountCount` riflemen.
- **A small UAS** (`is_unmanned`) — stowed on the carrier while the section is mounted, orbiting ahead of it at
  altitude once they are on the ground.

Every cycle is a keep-alive for all of them. Three things exist here to be tested against rather than to look
pretty:

- **`mount_host`.** The section alternates mounted (`MountedPhaseDuration`) and dismounted
  (`DismountedPhaseDuration`). While mounted every rifleman reports the carrier as its host and rides its
  position; dismounted they fan out over `DismountSpreadM` with no host at all. That appearing and disappearing
  relationship is what a client's map has to collapse, and it is easy to get wrong when it never changes.
- **Combined type flags.** `BlueForceType` allows several to be true at once, and all three kinds are present
  simultaneously — so an implementation that treats the flags as an enum is caught.
- **GNSS dropout.** With probability `GnssOutageProbability` per cycle the leader's handheld goes silent for
  `GnssOutageDuration` and the source reports *no* position at all. That is deliberate: it leaves the server's own
  staleness handling to show the client the fix going `is_invalid_or_expired` while keeping its coordinates —
  exactly the case the contract describes, "because the user entered a building".

This source is registered against two runners (`AddBlueForceSource` + `AddOwnPoseSource`) sharing one instance, so
that the position it reports over `OwnPose` is the same soldier's as the leader's blue force rather than a second
patrol clock drifting alongside the first. The two runners tick independently, so everything this class keeps
between cycles — the GNSS state machine and its `Random` — sits behind one gate; everything else is a pure
function of the options and the current time.

The leader (`blueforce:patrol:leader`) is the natural own force. `own_blue_force` has no field in
`UpdateBlueForce` — which blue force is "me" belongs to the system answering, not to the report — so point the
Host's `Simulator:BlueForce:OwnIdentity` at that identity to see it flagged (and ringed on the map GUI).

Symbol codes are illustrative MIL-STD-2525C with friend affiliation, the same convention the convoy and NWS
sources use for symbology the contract doesn't cover natively.

### Configuration (`BlueForcePatrolOptions`)

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Whether the source runs. |
| `UpdateInterval` | `00:00:05` | Keep-alive cadence. The contract's "at least every 30s" is the ceiling, not the target — this leaves the section alive across several lost calls. |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.8` | Center of the patrol area. |
| `PatrolRadiusM` | `1200` | Radius of the carrier's loop. |
| `LapDuration` | `00:12:00` | Time for one full lap. |
| `DismountCount` | `3` | Riflemen, not counting the leader. |
| `MountedPhaseDuration` | `00:03:00` | How long the section stays aboard the carrier. |
| `DismountedPhaseDuration` | `00:03:00` | How long it stays on the ground before remounting. |
| `DismountSpreadM` | `120` | How far the dismounts spread from the carrier. |
| `PositionSourceIdentifier` | `GNSS` | `source_identifier` the leader's fixes are reported under. |
| `GnssOutageProbability` | `0.05` | Chance per cycle that the GNSS drops out. |
| `GnssOutageDuration` | `00:00:45` | How long an outage lasts. |
| `Seed` | `7` | Deterministic seed for the outage rolls. |
| `Callsign` | `BADGER` | Callsign prefix for every member of the section. |

## `LoadGeneratorSource`

Config section: `Adapter:LoadGenerator`, bound to `LoadGeneratorOptions`. **Disabled by default.**

The others simulate a situation; this one applies pressure. `Simulator:Performance` on the Host has always had
knobs — subscriber channel capacity, overflow mode, stream batch size, object cap — with nothing in the repository
able to reach the conditions they govern. Running this against the Host with a subscriber attached is what makes
`tacticalapi_subscriber_events_dropped_total` move, and therefore what makes those knobs tunable rather than
guessable.

```bash
# 4000 objects, 500 reported every 100ms, against a Host with a small subscriber buffer.
Adapter__LoadGenerator__Enabled=true Adapter__LoadGenerator__ObjectCount=4000 \
Adapter__LoadGenerator__BatchSize=500 Adapter__LoadGenerator__UpdateInterval=00:00:00.100 \
Adapter__SyntheticScenario__Enabled=false \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Synthetic

curl -s http://localhost:4268/metrics | grep subscriber_events
```

### How it works

1. Objects are laid out on a square grid around the configured centre, so the map UI shows the load as a block
   rather than one overlapping dot.
2. Each cycle reports a rolling window of `BatchSize` objects, advancing through the population of `ObjectCount` and
   wrapping around. Reporting a subset rather than the whole population is both what real sources do and what keeps
   the two knobs independent: `BatchSize` sets how big each message is, `UpdateInterval` how often one is sent, and
   `ObjectCount` how large the situation grows — which would otherwise be capped by whatever fits in a single
   message (`Simulator:Performance:MaxReceiveMessageSizeMb` on the receiving end).
3. Every object drifts slightly each cycle. A load generator whose objects never moved would have its updates
   merged away as no-ops by any correct implementation, and would measure nothing.
4. Positions are a deterministic function of index and elapsed time — no RNG at all — so two runs with the same
   settings produce the same load, and a throughput comparison between them measures the change under test rather
   than the weather.

### Options

| Key                | Default             | Notes                                                          |
|--------------------|---------------------|----------------------------------------------------------------|
| `Enabled`          | `false`             | a deliberate stress tool, not a demo                            |
| `ObjectCount`      | `10000`             | population size, range 1–1,000,000                              |
| `BatchSize`        | `1000`              | objects reported per cycle; this is what bounds message size    |
| `UpdateInterval`   | `00:00:01`          | range 10ms–10min                                                |
| `CenterLatitude`   | `53.08`             |                                                                 |
| `CenterLongitude`  | `8.8`               |                                                                 |
| `SpreadDegrees`    | `1.0`               | half-width of the square the grid covers, range 0.001–90        |
| `TrackTimeToLive`  | `00:05:00`          | short values also exercise the expiry sweeper under load        |
| `SymbolCode`       | `SUGP-----------`   |                                                                 |
| `SymbolCatalog`    | `Mil2525C`          |                                                                 |
| `ReporterId`       | `SIM-LOAD`          |                                                                 |

Covered by `LoadGeneratorSourceTests` (windowing and wrap-around, movement between cycles, determinism, clean
ingest into the store).

## `GeometryShowcaseSource`

Config section: `Adapter:GeometryShowcase`, bound to `GeometryShowcaseOptions`. **Disabled by default** — it is a
test pattern, not demo content, so switch it on while checking a client's rendering. It is seven static objects,
so it costs nothing to run beside `SyntheticScenario`, but they carry no expiry: switched off again, they stay in
the Host until it restarts.

Exists for one question the scenario sources answer badly: *does my client draw every location kind correctly?*
They do emit polygons, ellipses and sketches, but buried in a moving picture and partly behind random incidents.
Here every shape is at a fixed offset around the configured center, derived only from the options — no RNG, no
motion — so two runs produce the same picture and a client's rendering can be compared against it directly.

### What it emits

| Identity                  | Object          | Location                                          |
|---------------------------|-----------------|---------------------------------------------------|
| `showcase:line`           | SketchDocument  | `Line`, three points (chevron)                    |
| `showcase:rectangle`      | SketchDocument  | `Polygon`, four corners, axis-aligned             |
| `showcase:circle`         | SketchDocument  | `Ellipse` with both axes equal                    |
| `showcase:ellipse`        | SketchDocument  | `Ellipse`, major axis rotated 45°, minor 2/5 of it |
| `showcase:multi`          | SketchDocument  | one sketch holding a line **and** a polygon **and** an ellipse |
| `showcase:symbol:line`    | Symbol          | `Line` — a symbol that is not a point             |
| `showcase:symbol:center`  | Symbol          | `Point`, the reference marker at the center       |

Each sketch element carries its own color, width and line style (solid/dash/dot), so a client's styling can be
checked at the same time. `showcase:multi` is the one that catches clients reading only the first sketch element.

The ellipse's conjugate diameter points are the **endpoints of the two semi-axes**: the first sits `ShapeSizeM / 2`
from the center along the rotation angle, the second 90° further round at `ShapeSizeM / 5`.

### Options

| Key                                  | Default         | Notes                                                    |
|--------------------------------------|-----------------|----------------------------------------------------------|
| `Enabled`                            | `true`          | class default; **appsettings.json ships this disabled**  |
| `UpdateInterval`                     | `00:00:10`      | range 500ms–1h; the shapes never change, this only re-reports them |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.8` | Bremen, like the other offline sources                   |
| `SpacingM`                           | `2000`          | center → each shape's own slot, range 100–100,000        |
| `ShapeSizeM`                         | `800`           | edge length/diameter, range 50–50,000                    |
| `ReporterId`                         | `SIM-SHAPES`    |                                                          |

Covered by `GeometryShowcaseSourceTests` (every location kind present, point counts, ellipse axis lengths and
orthogonality, stable identities across cycles).

## `EasternFlankSource`

Config section: `Adapter:EasternFlank`, bound to `EasternFlankOptions`. **Disabled by default** —
`appsettings.json` ships `SyntheticScenario` as the default picture. To show the theater instead, enable this and
disable `SyntheticScenario` (and `GeometryShowcase`, if on) so the demo map isn't competing with a test pattern.

A theater-scale, fictional wargame picture: a front line from the Gulf of Finland down to the Black Sea,
formations deployed along both sides of it, and strategic reinforcement flows feeding each side. The geography is
real; the forces, formations and movements are invented for the demo.

### The story it tells

The front is not static. One full `CycleDuration` covers four phases, then repeats:

1. The eastern side breaks through at the Polish border sector, with supporting attacks in the Ukrainian south
   and in Lithuania that start and finish at different times. The line bulges up to `MaxBulgeKm` west and the
   ground taken is drawn as a red **occupied territory** polygon; a `Breakthrough` event is raised at the peak.
2. Reinforcements arriving over the transatlantic route push the bulge back to the border.
3. The western side counter-attacks in the Latvian sector. The line bulges east and the ground taken is drawn as
   a blue **liberated territory** polygon; a `Counter-offensive` event is raised.
4. Eastern reserves arriving over the Trans-Siberian route and the Iranian supply line press it back to the
   border.

Six pushes with their own sectors, depths and windows overlap through the cycle, and a slow ripple runs along the
whole line, so the trace keeps changing shape instead of growing and shrinking as one smooth arc.

The SITREP text, the standing task's completion ratio and every formation's "attacking"/"holding" note follow the
same phase, so the map, the task list and the text all tell one story.

### What it emits (~140 objects at the default counts)

| Group                | Objects                                   | What it shows                                                                         |
|----------------------|-------------------------------------------|---------------------------------------------------------------------------------------|
| Front line           | 2 × `SketchDocument`                      | The pre-conflict border (dotted, for reference) and the current FLOT that moves with the fighting |
| Captured ground      | up to 2 × `SketchDocument`                | Polygons between border and FLOT, only while a side is ahead                          |
| Ground forces        | 2 × `FrontUnitsPerSide` Symbols           | Named formations deployed 45 km behind their own side. Each is a different branch — armor, mechanized infantry, artillery, rocket artillery, air defense, engineers, missiles, cavalry, signals, intelligence, medical, transport, supply — so the line reads as a combined arms force |
| Losses               | occasional deletions                      | A formation is destroyed every `CycleDuration / 6`: reported once with an expiry in the past so the object is deleted from the situation, plus a short-lived loss report; a replacement takes the sector in the next window, under the same identity — which works because a delete in this simulator is a soft delete of an object rather than a tombstone on its identity (see [Architecture](../../../docs/ARCHITECTURE.md#interface-semantics-implemented)) |
| Strategic flows      | `UsReinforcementCount` + `EasternReserveCount` Symbols | Transports on the transatlantic route (vessels at sea, ground transport once ashore) and rail echelons from the far east |
| Routes               | 3 × `Route`                               | SLOC AMBER (Norfolk → Warsaw), LOC GRANITE (Beijing → Minsk) and LOC SAFFRON (Tehran → Gomel), with named waypoints |
| Southern supply line | `SouthernSupplyCount` Symbols             | Materiel from Iran: overland to Bandar Anzali, by ship across the Caspian, then up the Volga corridor to the eastern staging area |
| Air                  | 2 × `AirPatrolsPerSide` Symbols           | Fighters, airborne early warning, tankers, attack helicopters, unmanned reconnaissance and bombers, each on its own station |
| Naval                | 20 Symbols over 17 stations               | Deliberately spread right across the world's oceans and mostly alone: carrier groups in the North Atlantic, the Barents and the North Pacific are the only places two hulls share a station, and beside them single ships work the South Atlantic, the South and Central Pacific, the Indian Ocean, the Arabian Sea, the Gulf of Aden, the Mediterranean, the Norwegian Sea, the Baltic and the Black Sea, plus a submarine on each side in deep water. Capped by `MaxVesselsPerNavalGroup` |
| Zones                | 12 × `SketchDocument`                     | A2/AD umbrellas and integrated air defense as circles, plus staging areas on both sides |
| Control measures     | 7 × `SketchDocument`                      | One of every area shape a client has to be able to draw: two named areas of interest and a restricted operations zone as **rectangles** — `Polygon` locations of exactly four corners, which is what tells a rectangle from a closed freehand area — two phase lines as **polylines** (`Line`), and two missile engagement zones as **ellipses** with unequal axes turned along the front (`Ellipse`), since every other ellipse here is a circle and a circle never exercises the major/minor/rotation handling. Planned graphics: they stay where they were drawn and the front moves through them |
| ORBAT                | 6 × `OrganizationUnit`                    | Corps and divisions per side (the contract's `UnitDesignation` stops at Regiment, so the echelon lives in the name) |
| Special forces       | 2 × `SpecialForcesTeamsPerSide` Symbols   | Ground, maritime and aviation teams 600–1500 km into the other side's hinterland, on what the far side of the theater runs on rather than just over the line: the air defense belt at Voronezh, the rail junction at Tula, the Northern Fleet in the Kola inlet and the Volga production complex on one side; the port of debarkation at Bremerhaven, the theater air hub at Ramstein, the Rotterdam approaches and the air base at Lakenheath on the other — drawn with the bare special operations battle dimension (`SFFP-----------` / `SHFP-----------`), not as ordinary infantry, so a client that derives its own type from the symbol code reads them back as SOF rather than as a ground unit a level deeper; the branch is named in the description — infiltrating → on the objective → exfiltrating |
| Satellites           | 5 Symbols                                 | Three western and two eastern reconnaissance satellites — imagery, signals collection, a sun-synchronous polar pass and an ocean surveillance orbit — ground tracks sweeping the globe |
| Unknown tracks       | `UnknownContactCount` Symbols             | Unclassified sensor contacts drifting across the line, drawn with the **unknown** affiliation |
| Neutral traffic      | `NeutralTrafficCount` Symbols             | Civilian shipping and relief convoys, drawn with the **neutral** affiliation           |
| Installations        | 33 Symbols                                | Air and naval bases, depots and logistics hubs on both sides — from Norfolk and Fort Bragg through Ramstein and Rzeszow to Severomorsk, Engels, Tartus and Vladivostok — plus civilian industry, ports, refineries and strait transits drawn neutral. Deliberately spread out: the front is the focus, but not the only thing on the map |
| Incidents            | 2 × `PictureDocument`                     | Sabotage, unmanned reconnaissance imagery and abductions, each with a picture attached; they arrive one at a time and expire |
| Reporting            | `ActionTask`, `TextDocument`, `ActionEvent` | Standing task with live completion, theater SITREP, breakthrough/counter-offensive events |

### Options

| Key                    | Default      | Notes                                                             |
|------------------------|--------------|--------------------------------------------------------------------|
| `Enabled`              | `true`       | class default; **appsettings.json ships this disabled**            |
| `UpdateInterval`       | `00:00:02`   | how often the picture is re-reported                               |
| `CycleDuration`        | `00:03:00`   | one full swing: breakthrough, pushed back, counter-attack, pushed back — about 45 s per phase |
| `MaxBulgeKm`           | `160`        | how deep a breakthrough goes at its peak, range 10–600             |
| `FrontUnitsPerSide`    | `8`          | formations along the line per side — deliberately sparse, so the line reads as a front rather than a wall of icons |
| `UsReinforcementCount` | `6`          | transports on the transatlantic route                              |
| `EasternReserveCount`  | `5`          | echelons on the eastern rail route                                 |
| `AirPatrolsPerSide`    | `3`          |                                                                    |
| `MaxVesselsPerNavalGroup` | `2`       | upper bound per station, range 0–2; most stations hold one ship anyway, so this only bites on the three two-hull groups |
| `SouthernSupplyCount`  | `5`          | transports on the Iranian supply line                              |
| `SpecialForcesTeamsPerSide` | `3`     | teams working objectives deep behind the other side's line, range 0–3 |
| `ShowSatellites`       | `true`       | three western, two eastern reconnaissance satellites               |
| `SatelliteOrbitDuration` | `00:03:00` | one orbit; short enough that a viewer sees a full pass             |
| `UnknownContactCount`  | `4`          | unclassified tracks, range 0–4                                     |
| `NeutralTrafficCount`  | `4`          | civilian shipping and relief convoys, range 0–4                    |
| `ShowInstallations`    | `true`       | bases, depots, plants, ports                                       |
| `ShowIncidents`        | `true`       | incident reports with imagery                                      |
| `ShowLosses`           | `true`       | formations destroyed and deleted from the situation                |
| `TrackTimeToLive`      | `00:05:00`   | expiry stamped on **every** object and pushed forward each cycle — see below |
| `ReporterId`           | `SIM-FLANK`  |                                                                    |

Sizing for the room: the default three-minute cycle is deliberately short — nobody watches a map for twenty
minutes waiting for a front to move, so the whole story plays out while someone is still looking at it. Raise
`CycleDuration` for a slower, more realistic tempo, or `FrontUnitsPerSide` for a denser line. The air and naval
orbits are derived from `CycleDuration`, so changing it speeds up or slows down the whole picture together. All
of it is hot-reloadable — an edit to `appsettings.json` takes effect on the next cycle, no restart.

All four MIL-STD-2525C affiliations are on the map at once — friendly, hostile, neutral and unknown — which is
worth knowing if a client renders them differently or filters on them.

The drawn geometry is graded rather than uniform: seven line weights between 1 and 8 pixels across the sketches,
following how binding a line is — the FLOT at 8, the pre-conflict border at 1, since that one is reference rather
than a control measure. That matters if a client's line width handling needs exercising: a picture drawn entirely at
one weight leaves it untested. Everything stays inside 1–10, which is the range the TAK adapter clamps to.

**Everything carries a rolling expiry.** Every object the source reports gets an expiry of `TrackTimeToLive` from
now, refreshed on every cycle, and not just the moving symbols: the ORBAT, the routes, the border, the zones and
the fixed installations get one too. Two things depend on it. A client that ages objects out by their expiry —
a TAK client's stale time, for one — would otherwise drop the static half of the picture off the map while the
simulator was still faithfully re-reporting it, because nothing about those objects ever changes. And since the
stamp moves forward every cycle, it is itself a real change on an otherwise identical object, so a consumer that
suppresses no-op updates still sees a heartbeat. The objects that manage their own lifetime keep it: a formation
reported destroyed carries an expiry in the past so the situation deletes it, and the loss reports, incident
pictures and the SITREP each have a window of their own. The flip side is that stopping the adapter now empties
the situation within `TrackTimeToLive` instead of leaving the static frame behind forever.

Nothing on the map sits perfectly still: even a holding formation sways between reports, because a picture where
half the symbols never move reads as frozen rather than live. Installations are the deliberate exception.

Covered by `EasternFlankSourceTests` (both sides and the strategic flows present with unique identities and
valid 15-character symbol codes, the line bulging west then back then east across one cycle, captured ground
appearing only while a side is ahead, transports moving along their route, satellites/special forces/
installations emitted in the expected numbers, all four affiliations present, everything except the
installations moving between two reports ten seconds apart, vessels never more than two to a station and spread
over both hemispheres, every object carrying an expiry that moves forward between cycles, and the objects with a
lifetime of their own keeping it).
