# CLAUDE.md

Simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC contract — all three services it declares: `Situation`, `BlueForceTracking`, `OwnPose`. The contract is the `external/tacticalapi` submodule; `Contracts.csproj` globs every `.proto` under it, so bumping the submodule is all it takes to get a new service's stubs.

**The invariants below are rules, not background.** Each one has been broken before. Follow them; the linked doc says why.

## Where to read before changing something

| Working on | Read first |
| --- | --- |
| Running Host + adapters, ports, map UI, grpcurl | [README.md](README.md#running) |
| Structure, service semantics, non-contract features | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Anything in an `appsettings.json` or an `Options` class | [docs/CONFIGURATION.md](docs/CONFIGURATION.md) |
| New data source, new situation object type | [docs/EXTENDING.md](docs/EXTENDING.md) |
| Tests | [docs/TESTING.md](docs/TESTING.md) |
| CI, running the pipeline locally | [docs/CI.md](docs/CI.md) |

Those docs are kept current — link to them, don't copy them here.

## Commands

```bash
dotnet build TacticalApi.Simulator.slnx                      # no .sln exists, only .slnx
dotnet test tests/TacticalApi.Simulator.Tests/TacticalApi.Simulator.Tests.csproj
dotnet test tests/TacticalApi.Simulator.Tests --filter "FullyQualifiedName~SyntheticScenarioSourceTests"

dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"   # CI's coverage gate
dotnet format --verify-no-changes --verbosity diagnostic                      # CI fails on this
nix run .#editorconfig-check    # every other tracked file (nixfmt + dprint + markdownlint + editorconfig-checker)
nix run .#ci-local              # the whole GitHub Actions workflow

# Conformance check of any implementation, all three services. Writes to the target;
# blue force + position checks CANNOT clean up (neither service has a delete RPC).
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --address http://localhost:5100 --include-slow
```

## Topology

```text
 Adapter.OpenSky   ┐
 Adapter.Nws       │   gRPC client call                  Host  (the only process that binds a port)
 Adapter.Synthetic ├──────────────────────────────────►  ├─ Situation ─────────► SituationStore
 Adapter.Replay    ┘   Adapter:Ingest:Address            ├─ BlueForceTracking ─► BlueForceStore
   one source per          (default: the Host;           ├─ OwnPose ───────────► OwnPoseStore
   process, no ports        repoint it to drive any      │
   of its own               other TacticalAPI impl)      └─ map UI · /metrics · /api/control · faults
                                                            (plain HTTP — never on the gRPC surface)

 Dependencies:  Host → Core → Contracts          Adapter.* → Sources.* → Core → Contracts
 The Host references no Sources.* project and has zero data sources. No Adapter.* references another.
```

**Which service does a thing go on?**

```text
 friendly element reporting its own position ──► BlueForceTracking   IBlueForceSource
 this platform's own fix ────────────────────► OwnPose              IOwnPoseSource
 anything reported *about* — hostiles,
 graphics, orders, messages ─────────────────► Situation            ISimulationSource
```

`ConvoyEscortSource` therefore emits gun trucks over `BlueForceTracking` but route/ambush/SALUTE over `Situation`; `CombatOutpostDefenseSource` splits OPs from its perimeter graphic. Never re-add a friendly vehicle as a `Symbol` "so it shows up too" — that is the same thing twice, from two services that disagree about what it is.

## Invariants

| Rule | Never |
| --- | --- |
| The generated `Rheinmetall.TacticalApi.V0` types are the model, in store, bus and sources. | Introduce an internal domain model to translate through. |
| gRPC surface = the contract, exactly. Faults, control endpoints and `/metrics` are Host HTTP paths. | Add an `rpc`. When the upstream contract grows a *service*, the Host implements it. |
| Source options bind to `AdapterOptions.SectionName + ":<Name>"` (`Core.Configuration`), e.g. `Adapter:ConvoyEscort`. | Bind under `SimulatorOptions` or anything `Sources`-shaped — that nesting is gone. |
| Options are `IOptionsMonitor`, `ValidateDataAnnotations().ValidateOnStart()`, re-read every cycle. `AppSettingsBootstrap` regenerates a missing file from the embedded copy. | Require a restart to pick up an edited `appsettings.json`. |
| A class may implement several source interfaces (`BlueForcePatrolSource`): register per service, `TryAddSingleton` keeps one instance. | Let it be constructed twice — its two reported positions then drift apart. |
| `BlueForceTracking` **replaces** whole objects (contract: "all fields must be filled in every call"). Delete is implicit (`BlueForceTimeoutSweeper`, `Simulator:BlueForce:KeepAliveTimeout`); `own_blue_force` / `associated_organization_unit_identity` come from `Simulator:BlueForce:OwnIdentity`. | "Fix" it to merge like `SituationStore`, or take the own-identity fields from the update. |
| `OwnPoseStore` keeps every `source_identifier`'s latest fix, answers with the primary (`Simulator:OwnPose:PrimarySource`). A stale fix keeps its coordinates and gains `is_invalid_or_expired` (`OwnPoseStalenessSweeper`). | Drop a stale fix — a subscriber who calls nothing would never learn. |
| `is_deleted` is a timestamped property merged by `PropertyMerge.Undelete`: a newer update revives the object. `SituationStoreTests` covers both directions. | Preserve `is_deleted` unconditionally — writes get acked while the object stays invisible forever. |
| `EventBroker<T>` (Core/Events) fans out for all three; `Situation`/`BlueForce`/`Position` brokers only pick instruments. | Merge their counters — keep-alive traffic would swamp the situation numbers. |
| `SituationObjectToUpdate` and `ReplayTimestamps` walk protobuf descriptors on purpose; `SituationObjectToUpdateTests` round-trips all eleven types. | "Simplify" them into switches over the object types — they rot silently when the contract grows a field. |
| Record/replay is `Situation` traffic only and lives outside the store: an `ISituationIngest` decorator (`Adapter:Recording`) or a subscriber (`Adapter:Recorder`). One JSON frame per line, `JsonFormatter.Default` (`Core/Recording/RecordingFormat.cs`). | Record keep-alives or positions; persist in `SituationStore`; use `WithIndentation(...)` — it breaks one-frame-per-line. |
| Metrics filter by meter **instance**: `ReferenceEquals(instrument.Meter, metrics.Meter)`. | Filter by meter name — simulators share the E2E process and scrapes bleed into each other. |

## Conventions

- File-scoped namespaces; `var` when the type is apparent (`.editorconfig`, `EnforceCodeStyleInBuild`).
- `TreatWarningsAsErrors` repo-wide (`Directory.Build.props`) — a warning fails the build.
- NuGet versions pinned centrally in `Directory.Packages.props`; no per-project `Version=`.
