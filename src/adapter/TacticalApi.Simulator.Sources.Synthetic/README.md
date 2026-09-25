# TacticalApi.Simulator.Sources.Synthetic

Eight fully offline sources — no network, no external dependency — registered via `AddSyntheticSources`.

| Source | Section (`Adapter:…`) | Default | What it is |
| --- | --- | --- | --- |
| `SyntheticAirTrackSource` | `SyntheticAirTracks` | off | Circular air-track picture |
| `SyntheticScenarioSource` | `SyntheticScenario` | **on** | Scripted mini scenario exercising all 11 object types |
| `ConvoyEscortSource` | `ConvoyEscort` | off | Convoy escort, ambushes resolved probabilistically |
| `CombatOutpostDefenseSource` | `CombatOutpostDefense` | off | Outpost defense, day/night tempo, both sides attrited |
| `BlueForcePatrolSource` | `BlueForcePatrol` | **on** | The only source feeding `BlueForceTracking` + `OwnPose` |
| `LoadGeneratorSource` | `LoadGenerator` | off | No plausibility, just pressure |
| `GeometryShowcaseSource` | `GeometryShowcase` | off | One static object per location kind, for checking a client's rendering |
| `EasternFlankSource` | `EasternFlank` | off | Theater-scale demo picture, meant to be shown to someone |

**Which service a thing goes on**, in every scenario here: a friendly element reporting its *own* position is a **blue force**; anything reported *about* — an enemy contact, a graphic someone drew, an order, a message — is a **situation object**. So gun trucks and observation posts go over `BlueForceTracking` while routes, perimeters, ambushes, hostiles, tasks and reports stay on `Situation`. Emitting a friendly vehicle on both would put it into a client's picture twice, from two services that disagree about what it is.

`GeoMath.cs` (destination-point projection, haversine distance, point-in-polygon) and `LanchesterModel.cs` (Lanchester's Square Law attrition) are shared by every scenario needing real geometry or engagement resolution.

## `SyntheticAirTrackSource`

`TrackCount` aircraft in circular orbits around a center point:

1. A seeded `Random` picks per track a fixed phase angle, an orbit radius (`RadiusKm × [0.5, 1.5)`) and a direction — deterministic for a given `Seed`.
2. Angular speed is `SpeedMetersPerSecond / radius`, so faster tracks and smaller orbits sweep proportionally faster.
3. Position is projected from the angle on a spherical approximation (`EarthRadiusKm = 6371.0`); course is tangential to the orbit.
4. Ids are `synthetic:air:{i:D3}`, names `SIM{i:D3}`; every cycle re-emits all tracks via `TrackUpdateFactory.CreateSymbolUpdate`.

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` (class default) | **`appsettings.json` ships this disabled** — `SyntheticScenario` is the out-of-the-box source |
| `UpdateInterval` | `00:00:02` | range 100ms–1h |
| `TrackCount` | `12` | range 1–10,000 |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80` | Bremen |
| `RadiusKm` | `60` | range 0.1–2000 |
| `SpeedMetersPerSecond` | `180` | range 1–3000 |
| `Seed` | `42` | deterministic picture per seed |
| `SymbolCode` | `SFAPMF---------` | friendly air, MIL-STD-2525C |
| `SymbolCatalog` | `Mil2525C` | |
| `TrackTimeToLive` | `00:00:30` | range 1s–1h |
| `ReporterId` | `SIM-SYNTH-AIR` | |

## `SyntheticScenarioSource`

**Enabled by default** — this is what populates the simulator out of the box. One coherent scenario per `UpdateInterval`, covering all 11 `oneof` object types and most location kinds in a single pass:

| Object type | What it represents |
| --- | --- |
| `OrganizationUnit` ×3 | A company HQ ("A Coy") with two subordinated platoons (ORBAT via `SubordinatedOrganizationUnitCollection`) |
| `Route` ×2 | "Route BRAVO", a six-checkpoint patrol loop as a `RouteLocation` (named waypoints with ETAs); "Route CHARLIE", a resupply air corridor as a 500 m `Corridor` |
| `Symbol` | A patrol vehicle interpolated along Route BRAVO (lap progress from `PatrolLapDuration`); "Objective HOTEL", a `Polygon` assembly area with a demo `ForeignKey` |
| `ActionTask` | The patrol order, `NotStarted → InProgress → Complete` with a live `CompletionRatio` tied to lap progress |
| `ActionEvent` | Incidents near the route, each with a location kind matching its nature: sniper attack (`Fan` detection arc), artillery (`Ellipse` impact area), booby-trap belt (`Multipoint` cluster), or a plain point. Probability `EventProbability` per cycle, expiring via `EventTimeToLive` |
| `TextDocument` | Periodic SITREP chat lines, probability `ChatProbability` |
| `NatoMessageDocument` | A static OWNSITREP MTF message |
| `PictureDocument` | A recon photo (tiny embedded 1×1 PNG — just enough to be a valid payload) |
| `VoiceMessageDocument` | A radio check (tiny embedded WAV) |
| `SketchDocument` | A multi-element planning sketch (`SketchLocation`): objective outline (dashed polygon), axis of advance (solid line), rally point — each individually colored and styled |
| `OverlayDocument` | An overlay carrying two nested phase-line `Symbol`s |

Everything goes through the same `UpdateSituationObject` batch path as any other source — indistinguishable from an external client. The patrol loop, objective area and FOB are laid out once at startup from a deterministic geometry builder, so shapes stay stable across cycles while the patrol symbol and incidents move.

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | |
| `UpdateInterval` | `00:00:05` | range 500ms–1h |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.80` | scenario anchor |
| `ExtentKm` | `20` | range 0.5–500 |
| `Seed` | `1337` | deterministic event generation |
| `EventProbability` | `0.25` | per cycle |
| `EventTimeToLive` | `00:03:00` | range 10s–24h |
| `ChatProbability` | `0.4` | per cycle |
| `PatrolLapDuration` | `00:10:00` | range 1min–24h |
| `ReporterId` | `SIM-SCENARIO` | |

## `ConvoyEscortSource`

A logistics convoy (callsign `TRIREME`: lead and trail gun trucks escorting `CargoVehicleCount` trucks) shuttles along `Route CONDOR`, turning around every `TransitDuration` — replacement personnel are assumed between runs, so casualties don't accumulate forever.

1. Three scripted high-risk zones sit at fixed fractions along the route (culvert, market chokepoint, rail underpass), each named in the `RouteLocation` waypoints. Ambush probability is `BaseAmbushProbability`, times `RiskZoneMultiplier` while the lead vehicle is within `RiskZoneRadiusM` of one — real ambush risk is not uniform, it clusters at terrain choke points.
2. On a triggered ambush (subject to `ContactCooldown`), a flip weighted by `IedProbabilityGivenContact` picks IED-initiated (an `Ellipse` blast area) vs. pure small-arms (a `Fan` engagement arc from stand-off). A hostile element (3–8 dismounts) spawns and `LanchesterModel` resolves it: IED contact gives the ambusher a surprise edge, small-arms favors the escort's training and firepower.
3. Friendly casualties reduce that vehicle's carried personnel (shown in its `AdditionalInformation`); any casualties raise a `CASEVAC request` `ActionTask` (`Priority1`) at the contact point.
4. Every cycle refreshes a persistent `NatoMessageDocument` carrying the latest contact as a SALUTE report (Size/Activity/Location/Unit/Time/Equipment).

| Over `BlueForceTracking` | Over `Situation` |
| --- | --- |
| `convoy:vehicle:{i}` — every gun truck and cargo truck, spaced along the route, `is_vehicle` (the lead also `is_leader`) | `convoy:route:condor`, with its risk-zone waypoints |
| | The ambush `ActionEvent` and the hostile symbols it spawns |
| | The CASEVAC `ActionTask`, the SALUTE `NatoMessageDocument` |

`BlueForce` has no free-text field, so a vehicle's state rides in its callsign — `TRIREME LOGPAC 2 [2 WIA]`, or `[COMBAT INEFFECTIVE]` once no one is left aboard, at which point it also reports zero speed. Full casualty accounting goes up in the SALUTE report. Point `Simulator:BlueForce:OwnIdentity` at `convoy:vehicle:0` (where the convoy commander rides) to see an own force.

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | |
| `UpdateInterval` | `00:00:05` | range 500ms–1h |
| `StartLatitude`/`StartLongitude` | `52.92`/`8.55` | one end of the route |
| `EndLatitude`/`EndLongitude` | `53.20`/`8.60` | the other |
| `TransitDuration` | `00:20:00` | one-way leg, range 5min–24h |
| `CargoVehicleCount` | `4` | range 1–20 |
| `SecurityVehicleCount` | `2` | range 1–8, split lead/trail |
| `PersonnelPerVehicle` | `4` | range 1–50 |
| `BaseAmbushProbability` | `0.01` | per cycle, away from a risk zone |
| `RiskZoneMultiplier` | `20.0` | range 1–200 |
| `RiskZoneRadiusM` | `300` | range 50–5000 |
| `IedProbabilityGivenContact` | `0.5` | IED vs. small-arms initiation |
| `ContactCooldown` | `00:03:00` | range 30s–1h |
| `Seed` | `2024` | deterministic zone layout and rolls |
| `ReporterId` | `SIM-CONVOY` | |

## `CombatOutpostDefenseSource`

"COP RESOLUTE" — an octagonal perimeter with `ObservationPostCount` OPs — probed by a persistent local hostile cell. Two things make it a simulation rather than scripted flavor:

1. **The clock matters.** Contact probability is `DayContactProbability`, times `NightContactProbabilityMultiplier` between `NightStartHourUtc` and `NightEndHourUtc` (against the real UTC hour) — irregular activity skews heavily toward darkness, reproduced directly rather than as a flat rate. The standing `Defend COP RESOLUTE` task's priority follows the same posture (`Priority1` at night, `Priority3` by day).
2. **Both sides have a memory.** Hostile cell and garrison strength are persistent pools (from `InitialHostileCellStrength`/`GarrisonStrength`) that deplete with casualties and slowly reconstitute (`HostileReinforcementPerHour`/`GarrisonReplacementPerHour`) — no infinite respawn, no instant recovery. Both are visible in the task's `AdditionalInformation`.

Given a contact (subject to `ContactCooldown`), one of three outcomes:

| Outcome | Resolution |
| --- | --- |
| **Indirect fire** (`ArtilleryFire`, `IndirectFireProbabilityGivenContact`) | A mortar-style `Ellipse` impact at a random bearing/range. `GeoMath.Contains` tests it against the actual perimeter polygon — inside the wire risks casualties, outside is a near-miss. A real technique, not a coin flip. |
| **Ground assault** (`Ambush`, `AssaultProbabilityGivenContact`) | 40–70% of the cell advances along a `Fan` axis; half the garrison stands to. `LanchesterModel` resolves it; 3+ friendly casualties raise a `QRF reinforcement` task. |
| **Harassing/sniper fire** (`SniperAttack`, the remainder) | A small element fires from stand-off (`Fan` arc) at the nearest OP; resolved the same way, defender-favored. |

A persistent `NatoMessageDocument` SITREP (posture + latest contact) refreshes every cycle.

| Over `BlueForceTracking` | Over `Situation` |
| --- | --- |
| `cop:bf:cp` — the command post, `is_leader`, garrison strength in its callsign | `cop:perimeter` — a graphic the commander drew, not a force |
| `cop:bf:op:{i}` — every manned observation post | `cop:task:defend`, `cop:task:qrf`, the hostile elements, the SITREP |

Point `Simulator:BlueForce:OwnIdentity` at `cop:bf:cp` to see an own force.

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | |
| `UpdateInterval` | `00:00:10` | range 500ms–1h |
| `CenterLatitude`/`CenterLongitude` | `53.00`/`9.05` | perimeter center |
| `PerimeterRadiusM` | `250` | range 50–2000 |
| `ObservationPostCount` | `4` | range 1–12 |
| `GarrisonStrength` | `40` | starting/max personnel, range 1–500 |
| `InitialHostileCellStrength` | `25` | starting/max hostile strength, range 1–500 |
| `DayContactProbability` | `0.02` | baseline, per cycle |
| `NightContactProbabilityMultiplier` | `5.0` | range 1–50 |
| `NightStartHourUtc`/`NightEndHourUtc` | `19`/`6` | UTC hours, wraps past midnight |
| `AssaultProbabilityGivenContact` | `0.08` | given a contact |
| `IndirectFireProbabilityGivenContact` | `0.4` | given a non-assault contact |
| `HostileReinforcementPerHour` | `0.6` | range 0–50 |
| `GarrisonReplacementPerHour` | `0.3` | range 0–50 |
| `ContactCooldown` | `00:08:00` | range 30s–4h |
| `Seed` | `4077` | deterministic layout and rolls |
| `ReporterId` | `SIM-COP` | |

## `BlueForcePatrolSource`

**Enabled by default** — the only source putting anything behind `BlueForceTracking` and `OwnPose`, so the Host plus this adapter shows all three services alive with no further configuration. The convoy and outpost scenarios feed `BlueForceTracking` too, but this is the one to reach for when the blue force contract itself is under test: only it exercises `mount_host`, an unmanned blue force, and `OwnPose` — including a position source that drops out.

It is a blue force source, not a simulation source, and that distinction is the point. A blue force is not a situation object with a friendly affiliation: it is a friendly participant updating its *own* position on a keep-alive cadence, with its own service, message and implicit deletion rule. So the scenarios above own the situation picture and this one owns the blue force picture.

A section ("BADGER") patrols a loop: a **carrier vehicle** (`is_vehicle`) driving one lap per `LapDuration`, a **section leader** (`is_leader`) with `DismountCount` riflemen, and a **small UAS** (`is_unmanned`) stowed while mounted, orbiting ahead once they are on the ground. Every cycle is a keep-alive for all of them.

Three things exist to be tested against rather than to look pretty:

| | |
| --- | --- |
| **`mount_host`** | The section alternates mounted (`MountedPhaseDuration`) and dismounted (`DismountedPhaseDuration`). Mounted, every rifleman reports the carrier as host and rides its position; dismounted they fan out over `DismountSpreadM` with no host. That appearing and disappearing relationship is what a client's map has to collapse — easy to get wrong when it never changes. |
| **Combined type flags** | `BlueForceType` allows several at once, and all three kinds are present simultaneously — so an implementation treating the flags as an enum is caught. |
| **GNSS dropout** | With probability `GnssOutageProbability` the leader's handheld goes silent for `GnssOutageDuration` and the source reports *no* position. Deliberate: it leaves the server's own staleness handling to show the fix going `is_invalid_or_expired` while keeping its coordinates — the contract's "because the user entered a building". |

Registered against two runners (`AddBlueForceSource` + `AddOwnPoseSource`) sharing **one** instance, so the position reported over `OwnPose` is the same soldier's as the leader's blue force rather than a second patrol clock drifting alongside. The runners tick independently, so everything kept between cycles — the GNSS state machine and its `Random` — sits behind one gate; everything else is a pure function of options and time.

The leader (`blueforce:patrol:leader`) is the natural own force: `own_blue_force` has no field in `UpdateBlueForce`, so point the Host's `Simulator:BlueForce:OwnIdentity` at that identity to see it flagged (and ringed on the map UI). Symbol codes are illustrative MIL-STD-2525C with friend affiliation, the same convention the convoy and NWS sources use.

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | |
| `UpdateInterval` | `00:00:05` | Keep-alive cadence. The contract's "at least every 30s" is the ceiling, not the target — this survives several lost calls. |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.8` | Center of the patrol area. |
| `PatrolRadiusM` | `1200` | Radius of the carrier's loop. |
| `LapDuration` | `00:12:00` | One full lap. |
| `DismountCount` | `3` | Riflemen, not counting the leader. |
| `MountedPhaseDuration` | `00:03:00` | Time aboard the carrier. |
| `DismountedPhaseDuration` | `00:03:00` | Time on the ground before remounting. |
| `DismountSpreadM` | `120` | How far dismounts spread from the carrier. |
| `PositionSourceIdentifier` | `GNSS` | `source_identifier` for the leader's fixes. |
| `GnssOutageProbability` | `0.05` | Per cycle. |
| `GnssOutageDuration` | `00:00:45` | |
| `Seed` | `7` | Deterministic outage rolls. |
| `Callsign` | `BADGER` | Prefix for every member of the section. |

## `LoadGeneratorSource`

The others simulate a situation; this one applies pressure. `Simulator:Performance` has always had knobs — channel capacity, overflow mode, batch size, object cap — with nothing in the repo able to reach the conditions they govern. Running this with a subscriber attached is what makes `tacticalapi_subscriber_events_dropped_total` move, and therefore what makes those knobs tunable rather than guessable.

```bash
# 4000 objects, 500 reported every 100ms, against a Host with a small subscriber buffer.
Adapter__LoadGenerator__Enabled=true Adapter__LoadGenerator__ObjectCount=4000 \
Adapter__LoadGenerator__BatchSize=500 Adapter__LoadGenerator__UpdateInterval=00:00:00.100 \
Adapter__SyntheticScenario__Enabled=false \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Synthetic

curl -s http://localhost:4268/metrics | grep subscriber_events
```

1. Objects sit on a square grid around the center, so the map UI shows the load as a block rather than one overlapping dot.
2. Each cycle reports a rolling window of `BatchSize`, advancing through `ObjectCount` and wrapping. Reporting a subset is what real sources do *and* what keeps the knobs independent: `BatchSize` sets message size, `UpdateInterval` the rate, `ObjectCount` how large the situation grows — otherwise capped by whatever fits in one message (`Simulator:Performance:MaxReceiveMessageSizeMb`).
3. Every object drifts each cycle. A load generator whose objects never moved would have its updates merged away as no-ops by any correct implementation, and would measure nothing.
4. Positions are a deterministic function of index and elapsed time — no RNG — so a throughput comparison between two runs measures the change under test rather than the weather.

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | a stress tool, not a demo |
| `ObjectCount` | `10000` | range 1–1,000,000 |
| `BatchSize` | `1000` | per cycle; this is what bounds message size |
| `UpdateInterval` | `00:00:01` | range 10ms–10min |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.8` | |
| `SpreadDegrees` | `1.0` | half-width of the grid square, range 0.001–90 |
| `TrackTimeToLive` | `00:05:00` | short values also exercise the expiry sweeper under load |
| `SymbolCode` | `SUGP-----------` | |
| `SymbolCatalog` | `Mil2525C` | |
| `ReporterId` | `SIM-LOAD` | |

`LoadGeneratorSourceTests` covers windowing and wrap-around, movement between cycles, determinism, and clean ingest into the store.

## `GeometryShowcaseSource`

A test pattern, not demo content: switch it on while checking a client's rendering. Seven static objects, so it costs nothing beside `SyntheticScenario` — but they carry **no expiry**, so switched off again they stay in the Host until it restarts.

It exists for one question the scenario sources answer badly: *does my client draw every location kind correctly?* They do emit polygons, ellipses and sketches, but buried in a moving picture and partly behind random incidents. Here every shape is at a fixed offset from the center, derived only from the options — no RNG, no motion — so two runs produce the same picture and a client's rendering can be compared against it directly.

| Identity | Object | Location |
| --- | --- | --- |
| `showcase:line` | SketchDocument | `Line`, three points (chevron) |
| `showcase:rectangle` | SketchDocument | `Polygon`, four corners, axis-aligned |
| `showcase:circle` | SketchDocument | `Ellipse`, both axes equal |
| `showcase:ellipse` | SketchDocument | `Ellipse`, major axis rotated 45°, minor 2/5 of it |
| `showcase:multi` | SketchDocument | one sketch holding a line **and** a polygon **and** an ellipse |
| `showcase:symbol:line` | Symbol | `Line` — a symbol that is not a point |
| `showcase:symbol:center` | Symbol | `Point`, the reference marker at the center |

Each sketch element carries its own color, width and line style (solid/dash/dot), so styling can be checked at the same time. `showcase:multi` is the one that catches clients reading only the first sketch element. The ellipse's conjugate diameter points are the **endpoints of the two semi-axes**: the first `ShapeSizeM / 2` from the center along the rotation angle, the second 90° further round at `ShapeSizeM / 5`.

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | class default; **appsettings.json ships this disabled** |
| `UpdateInterval` | `00:00:10` | range 500ms–1h; the shapes never change, this only re-reports them |
| `CenterLatitude` / `CenterLongitude` | `53.08` / `8.8` | Bremen, like the other offline sources |
| `SpacingM` | `2000` | center → each shape's slot, range 100–100,000 |
| `ShapeSizeM` | `800` | edge length/diameter, range 50–50,000 |
| `ReporterId` | `SIM-SHAPES` | |

`GeometryShowcaseSourceTests` covers every location kind present, point counts, ellipse axis lengths and orthogonality, and stable identities across cycles.

## `EasternFlankSource`

A theater-scale fictional wargame picture: a front line from the Gulf of Finland to the Black Sea, formations along both sides, and strategic reinforcement flows feeding each. The geography is real; the forces and movements are invented for the demo. To show it, enable this and disable `SyntheticScenario` (and `GeometryShowcase`, if on) so the demo map isn't competing with a test pattern.

### The story it tells

One `CycleDuration` covers four phases, then repeats:

1. The eastern side breaks through at the Polish border sector, with supporting attacks in the Ukrainian south and in Lithuania starting and finishing at different times. The line bulges up to `MaxBulgeKm` west, the ground taken is drawn as a red **occupied territory** polygon, and a `Breakthrough` event is raised at the peak.
2. Reinforcements over the transatlantic route push the bulge back to the border.
3. The western side counter-attacks in the Latvian sector; the line bulges east, ground taken is drawn as a blue **liberated territory** polygon, and a `Counter-offensive` event is raised.
4. Eastern reserves over the Trans-Siberian route and the Iranian supply line press it back to the border.

Six pushes with their own sectors, depths and windows overlap through the cycle, and a slow ripple runs along the whole line, so the trace keeps changing shape instead of growing and shrinking as one smooth arc. The SITREP text, the standing task's completion ratio and every formation's "attacking"/"holding" note follow the same phase, so the map, the task list and the text tell one story.

### What it emits (~140 objects at the defaults)

| Group | Objects | What it shows |
| --- | --- | --- |
| Front line | 2 × `SketchDocument` | The pre-conflict border (dotted, reference) and the current FLOT, moving with the fighting |
| Captured ground | up to 2 × `SketchDocument` | Polygons between border and FLOT, only while a side is ahead |
| Ground forces | 2 × `FrontUnitsPerSide` Symbols | Named formations 45 km behind their own side, each a different branch — armor, mechanized infantry, artillery, rocket artillery, air defense, engineers, missiles, cavalry, signals, intelligence, medical, transport, supply — so the line reads as a combined arms force |
| Losses | occasional deletions | A formation is destroyed every `CycleDuration / 6`: reported once with an expiry in the past so the situation deletes it, plus a short-lived loss report; a replacement takes the sector next window **under the same identity** — which works because a delete here is a soft delete of an object, not a tombstone on its identity ([Architecture](../../../docs/ARCHITECTURE.md#interface-semantics-implemented)) |
| Strategic flows | `UsReinforcementCount` + `EasternReserveCount` Symbols | Transports on the transatlantic route (vessels at sea, ground transport once ashore) and rail echelons from the far east |
| Routes | 3 × `Route` | SLOC AMBER (Norfolk → Warsaw), LOC GRANITE (Beijing → Minsk), LOC SAFFRON (Tehran → Gomel), with named waypoints |
| Southern supply line | `SouthernSupplyCount` Symbols | Materiel from Iran: overland to Bandar Anzali, by ship across the Caspian, then up the Volga corridor |
| Air | 2 × `AirPatrolsPerSide` Symbols | Fighters, AEW, tankers, attack helicopters, unmanned recon and bombers, each on station |
| Naval | 20 Symbols over 17 stations | Deliberately spread across the world's oceans and mostly alone: carrier groups in the North Atlantic, Barents and North Pacific are the only two-hull stations; beside them single ships work the South Atlantic, South and Central Pacific, Indian Ocean, Arabian Sea, Gulf of Aden, Mediterranean, Norwegian Sea, Baltic and Black Sea, plus a submarine per side in deep water. Capped by `MaxVesselsPerNavalGroup` |
| Zones | 12 × `SketchDocument` | A2/AD umbrellas and integrated air defense as circles, plus staging areas both sides |
| Control measures | 7 × `SketchDocument` | One of every area shape a client must draw: two areas of interest and a restricted operations zone as **rectangles** (`Polygon`s of exactly four corners — what tells a rectangle from a closed freehand area), two phase lines as **polylines** (`Line`), and two missile engagement zones as **ellipses** with unequal axes turned along the front, since every other ellipse here is a circle and a circle never exercises major/minor/rotation handling. Planned graphics: they stay where drawn and the front moves through them |
| ORBAT | 6 × `OrganizationUnit` | Corps and divisions per side (the contract's `UnitDesignation` stops at Regiment, so the echelon lives in the name) |
| Special forces | 2 × `SpecialForcesTeamsPerSide` Symbols | Teams 600–1500 km into the other side's hinterland, on what the far side runs on rather than just over the line: the Voronezh air defense belt, the Tula rail junction, the Northern Fleet in the Kola inlet, the Volga production complex; Bremerhaven's port of debarkation, the Ramstein air hub, the Rotterdam approaches, Lakenheath. Drawn with the bare special operations battle dimension (`SFFP-----------` / `SHFP-----------`) so a client deriving type from the symbol code reads them back as SOF, not as a ground unit a level deeper; branch named in the description — infiltrating → on the objective → exfiltrating |
| Satellites | 5 Symbols | Three western, two eastern: imagery, signals collection, a sun-synchronous polar pass, an ocean surveillance orbit — ground tracks sweeping the globe |
| Unknown tracks | `UnknownContactCount` Symbols | Unclassified sensor contacts drifting across the line, **unknown** affiliation |
| Neutral traffic | `NeutralTrafficCount` Symbols | Civilian shipping and relief convoys, **neutral** affiliation |
| Installations | 33 Symbols | Air and naval bases, depots and logistics hubs both sides — Norfolk and Fort Bragg through Ramstein and Rzeszow to Severomorsk, Engels, Tartus and Vladivostok — plus civilian industry, ports, refineries and strait transits drawn neutral. Deliberately spread out: the front is the focus, not the only thing on the map |
| Incidents | 2 × `PictureDocument` | Sabotage, unmanned recon imagery and abductions, each with a picture; they arrive one at a time and expire |
| Reporting | `ActionTask`, `TextDocument`, `ActionEvent` | Standing task with live completion, theater SITREP, breakthrough/counter-offensive events |

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | class default; **appsettings.json ships this disabled** |
| `UpdateInterval` | `00:00:02` | how often the picture is re-reported |
| `CycleDuration` | `00:03:00` | one full swing, about 45s per phase |
| `MaxBulgeKm` | `160` | breakthrough depth at its peak, range 10–600 |
| `FrontUnitsPerSide` | `8` | deliberately sparse, so the line reads as a front rather than a wall of icons |
| `UsReinforcementCount` | `6` | transports on the transatlantic route |
| `EasternReserveCount` | `5` | echelons on the eastern rail route |
| `AirPatrolsPerSide` | `3` | |
| `MaxVesselsPerNavalGroup` | `2` | range 0–2; most stations hold one ship anyway, so this only bites on the three two-hull groups |
| `SouthernSupplyCount` | `5` | transports on the Iranian supply line |
| `SpecialForcesTeamsPerSide` | `3` | range 0–3 |
| `ShowSatellites` | `true` | |
| `SatelliteOrbitDuration` | `00:03:00` | short enough that a viewer sees a full pass |
| `UnknownContactCount` | `4` | range 0–4 |
| `NeutralTrafficCount` | `4` | range 0–4 |
| `ShowInstallations` | `true` | bases, depots, plants, ports |
| `ShowIncidents` | `true` | incident reports with imagery |
| `ShowLosses` | `true` | formations destroyed and deleted from the situation |
| `TrackTimeToLive` | `00:05:00` | expiry stamped on **every** object and pushed forward each cycle — see below |
| `ReporterId` | `SIM-FLANK` | |

### Notes for showing it

- **Sizing for the room.** The three-minute cycle is deliberately short — nobody watches a map for twenty minutes waiting for a front to move. Raise `CycleDuration` for a realistic tempo or `FrontUnitsPerSide` for a denser line. Air and naval orbits derive from `CycleDuration`, so it speeds up or slows the whole picture together. All hot-reloadable: an edit takes effect next cycle.
- **All four MIL-STD-2525C affiliations** are on the map at once — friendly, hostile, neutral, unknown — worth knowing if a client renders or filters on them.
- **Line weights are graded, not uniform**: seven weights between 1 and 8 px across the sketches, following how binding a line is — the FLOT at 8, the pre-conflict border at 1, since that one is reference rather than a control measure. A picture drawn entirely at one weight leaves a client's width handling untested. Everything stays inside 1–10, the range the TAK adapter clamps to.
- **Everything carries a rolling expiry** of `TrackTimeToLive`, refreshed every cycle — not just moving symbols but the ORBAT, routes, border, zones and fixed installations. Two things depend on it: a client that ages objects out by expiry (a TAK client's stale time) would otherwise drop the static half of the picture while the simulator was still re-reporting it, since nothing about those objects ever changes; and because the stamp moves forward, it is itself a real change on an otherwise identical object, so a consumer suppressing no-op updates still sees a heartbeat. Objects managing their own lifetime keep it — a destroyed formation carries an expiry in the past, and loss reports, incident pictures and the SITREP each have their own window. The flip side: stopping the adapter empties the situation within `TrackTimeToLive` instead of leaving the static frame behind forever.
- **Nothing sits perfectly still.** Even a holding formation sways between reports — a picture where half the symbols never move reads as frozen rather than live. Installations are the deliberate exception.

`EasternFlankSourceTests` covers both sides and the strategic flows present with unique identities and valid 15-character symbol codes; the line bulging west, back, then east across one cycle; captured ground appearing only while a side is ahead; transports moving along their route; satellites, special forces and installations emitted in the expected numbers; all four affiliations present; everything except installations moving between two reports ten seconds apart; vessels never more than two to a station and spread over both hemispheres; every object carrying an expiry that moves forward between cycles; and the objects with a lifetime of their own keeping it.
