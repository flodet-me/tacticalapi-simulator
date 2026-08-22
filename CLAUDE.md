# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A simulator for the [Rheinmetall TacticalAPI](https://github.com/Rheinmetall/tacticalapi) gRPC interface (`rheinmetall.tactical_api.v0.Situation`). Full details, running instructions and endpoints: [README.md](README.md). Deeper docs live in `docs/`:

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — design principles, solution layout, interface semantics implemented
- [docs/CONFIGURATION.md](docs/CONFIGURATION.md) — `appsettings.json` reference per executable
- [docs/EXTENDING.md](docs/EXTENDING.md) — adding a data source, adding a situation object type
- [docs/TESTING.md](docs/TESTING.md) — unit/E2E test layers
- [docs/CI.md](docs/CI.md) — pipeline stages, running the whole pipeline locally with `act`

Read the relevant doc above before making a structural change in that area — these are kept current and this file deliberately doesn't repeat their content.

## Commands

```bash
# Build / test everything (there is no .sln, only the .slnx)
dotnet build TacticalApi.Simulator.slnx
dotnet test tests/TacticalApi.Simulator.Tests/TacticalApi.Simulator.Tests.csproj

# Single test (xunit filter, works with dotnet test too)
dotnet test tests/TacticalApi.Simulator.Tests --filter "FullyQualifiedName~SyntheticScenarioSourceTests"

# Check any TacticalAPI implementation against the contract (writes to its situation)
dotnet run --project src/tools/TacticalApi.Simulator.Tool.Conformance -- --address http://localhost:5100 --include-slow

# Same coverage gate as CI
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"

# Format check (CI fails the build on this, not just style-suggests)
dotnet format --verify-no-changes --verbosity diagnostic

# Format check for every other tracked file against .editorconfig (nixfmt + editorconfig-checker; see docs/CI.md)
nix run .#editorconfig-check

# Run the whole GitHub Actions workflow locally (see docs/CI.md for caveats)
nix run .#ci-local
```

Running the app itself (Host + adapters, ports, map UI, grpcurl examples) is documented in [README.md](README.md#running) — don't duplicate it here.

## Architecture — the parts that span multiple files

- **The proto model IS the model.** Generated `Rheinmetall.TacticalApi.V0` types are used directly in the store, event bus, and sources — there is no internal domain model to translate through.
- **Host and adapters are separate executables, one store.** `TacticalApi.Simulator.Host` is only the store + `Situation` gRPC service + map UI — it has zero data sources. Each source runs in its own `TacticalApi.Simulator.Adapter.*` process and pushes updates into whichever endpoint `Adapter:Ingest:Address` points at, via a real `Situation.SituationClient` gRPC call (default: the Host). Repointing that one setting drives an adapter against any other TacticalAPI implementation instead, independently of the others.
- **Adapter config has its own root, separate from the Host's.** Each `Adapter.*`'s own `appsettings.json` has the source's settings directly under `Adapter:<SourceName>` (e.g. `Adapter:ConvoyEscort`), not nested under a `Sources` key — that nesting existed only when the Host held every source's config in one shared file, before the Host/adapter split, and has since been removed. `Adapter` and the Host's own `Simulator` root are deliberately separate sections (an adapter isn't the simulator, it's a process that feeds one) - when adding or fixing a source's `Options` class, bind to `AdapterOptions.SectionName + ":<Name>"` (`TacticalApi.Simulator.Core.Configuration`), not `SimulatorOptions` or anything `Sources`-shaped. The Host's own `appsettings.json` has no source config at all.
- **Options are `IOptionsMonitor`, hot-reloadable.** Bound with `ValidateDataAnnotations().ValidateOnStart()`, re-read every cycle — no restart needed to pick up an edited `appsettings.json`. If the file is missing at startup, `AppSettingsBootstrap` (Core) regenerates it from the executable's own embedded copy.
- **Dependency direction:** `Host → Core → Contracts`, and separately `Adapter.* → Sources.* → Core → Contracts`. The Host never references any `Sources.*` project; no `Adapter.*` project references another.
- **Adding a data source or a new situation object type**: follow [docs/EXTENDING.md](docs/EXTENDING.md) exactly — it has working code templates for both (`ISimulationSource` + adapter project shape; `ISituationObjectMerger` for a new oneof case).
- **Nothing that isn't in the contract goes on the `Situation` service.** Fault injection (`Simulator:Faults`, `Host/Faults/`), the control endpoints (`Simulator:Control`, `Host/Control/`) and `/metrics` are all Host features on plain HTTP paths. Adding an RPC would let a client depend on something no real implementation offers. New capability of that kind belongs beside the existing ones, never as a new `rpc`.
- **Two places are descriptor-driven, on purpose.** `SituationObjectToUpdate` (Core, turns a stored object back into the update that recreates it — the basis of stream-side recording) and `ReplayTimestamps` walk the protobuf descriptors instead of switching over the eleven object types, because the stored and update message families mirror each other field for field and a hand-written mapping would silently rot when the upstream contract grows a field. Don't "simplify" them into switches. `SituationObjectToUpdateTests` round-trips all eleven types through store → update → store and is what keeps them honest.
- **Record/replay is outside the store.** `SituationStore` stays persistence-free; recording happens either as an `ISituationIngest` decorator in an adapter (`Adapter:Recording`, lossless) or as a subscriber (`Adapter:Recorder`, works against any implementation). Format: one JSON frame per line, objects in canonical protobuf JSON (`Core/Recording/RecordingFormat.cs`) — note `JsonFormatter.Default`, since any `WithIndentation(...)` switches the formatter to multi-line and breaks the one-frame-per-line invariant.
- **Metrics filter by meter instance, not meter name.** `SimulatorMetrics` is a plain BCL `Meter` (no OpenTelemetry dependency); `MetricsCollector` and the test recorder both match on `ReferenceEquals(instrument.Meter, metrics.Meter)`. Several simulators share the E2E test process, and name-based filtering made each one's scrape include every other one's numbers.

## Conventions

- File-scoped namespaces, `var` preferred when the type is apparent (`.editorconfig`, enforced as warnings/errors via `EnforceCodeStyleInBuild`).
- `TreatWarningsAsErrors` is on repo-wide (`Directory.Build.props`) — a warning fails the build, not just CI's separate format-check step.
- NuGet versions are centrally pinned in `Directory.Packages.props`; don't add per-project `Version=` attributes on `PackageReference`.
