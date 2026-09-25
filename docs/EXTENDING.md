# Extending the simulator

Two nearby things this document is *not* about:

| Not this | Because |
| --- | --- |
| Descriptor-driven code (`SituationObjectToUpdate`, `ReplayTimestamps`) | Needs no work when a new object type appears — that is why it is written that way ([Architecture](ARCHITECTURE.md#interface-semantics-implemented)). `Situation` only; recordings are `Situation` traffic, deliberately. |
| A Host capability outside the contract (another fault, another control endpoint) | Belongs beside the existing ones in `Host/Faults/` or `Host/Control/`, never as a new RPC. |

## Adding your own data source (e.g. an AIS ship tracker)

Worked examples to read alongside this: [`Sources.OpenSky`](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) for a live HTTP-polling single-type source, [`Sources.Nws`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) when one feed yields several object kinds — `ProduceAsync` just returns a mixed batch, there is no multi-type mechanism.

**1. Implement `ISimulationSource`.** For track-like data, `TrackReport` + `TrackUpdateFactory.CreateSymbolUpdate(...)` does the TacticalAPI mapping:

```csharp
public sealed class AisShipSource(IHttpClientFactory http, IOptionsMonitor<AisOptions> options, TimeProvider time)
    : ISimulationSource
{
    public string Name => "AIS";
    public bool Enabled => options.CurrentValue.Enabled;
    public TimeSpan Interval => options.CurrentValue.PollInterval;

    public async Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken ct)
    {
        var ships = await FetchShipsAsync(ct);
        var now = time.GetUtcNow();
        return ships.Select(s => TrackUpdateFactory.CreateSymbolUpdate(
            new TrackReport($"ais:{s.Mmsi}", s.Name, s.Lat, s.Lon, 0, s.Course, s.SpeedMs, $"MMSI {s.Mmsi}"),
            "SIM-AIS", "SNSP-----------", SymbolCatalog.Mil2525C, now, options.CurrentValue.TrackTimeToLive)).ToList();
    }
}
```

**2. Register it with its options** in an `AddXyzSources` extension (template: `OpenSkyServiceCollectionExtensions.AddOpenSkySources`):

```csharp
public static IServiceCollection AddAisSources(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<AisOptions>().Bind(configuration.GetSection(AisOptions.SectionName))
        .ValidateDataAnnotations().ValidateOnStart();
    services.AddSimulationSource<AisShipSource>();
    return services;
}
```

**3. Give it its own adapter executable** — `src/adapter/TacticalApi.Simulator.Adapter.Ais` (plain `Microsoft.NET.Sdk`, `<OutputType>Exe</OutputType>`), referencing only `Core` (`..\..\simulator\...` — Core lives under `src/simulator/`) and your `Sources.Ais` sibling. The whole `Program.cs`:

```csharp
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.Ais;

AdapterHost.Run(args, (services, configuration) => services.AddAisSources(configuration));
```

Plus its own `appsettings.json` with `Adapter:Ingest:Address` and `Adapter:Ais`, included **both** as `Content` (copied next to the executable) and as `<EmbeddedResource Include="appsettings.json"/>` (so `AppSettingsBootstrap` can regenerate that exact file — see [Configuration](CONFIGURATION.md)). Copy the shape from [`Adapter.OpenSky`](../src/adapter/TacticalApi.Simulator.Adapter.OpenSky), and add the project to `TacticalApi.Simulator.slnx`.

Each source gets its own `SimulationSourceRunner` background service — a slow or failing source never stalls another, exceptions are logged and retried next cycle — and its own adapter process. It never touches the Host, which has no sources.

## Adding a blue force or own-position source

`IBlueForceSource` produces `UpdateBlueForce`s; `IOwnPoseSource` produces one `UpdatePosition` or null. Scheduling, enable/disable and failure handling come from the same `SourceRunner<T>`, so only the return type differs:

```csharp
public sealed class MyTrackerSource(IOptionsMonitor<MyOptions> options, TimeProvider time) : IBlueForceSource
{
    public string Name => "MyTracker";
    public bool Enabled => options.CurrentValue.Enabled;

    // The call IS the keep-alive, so this must stay well under the implementation's
    // timeout. The contract asks for at least every 30s; treat that as the ceiling.
    public TimeSpan Interval => options.CurrentValue.UpdateInterval;

    public Task<IReadOnlyList<UpdateBlueForce>> ProduceBlueForcesAsync(CancellationToken ct) => ...;
}
```

Register with `services.AddBlueForceSource<MyTrackerSource>()` (or `AddOwnPoseSource<...>`), exactly as for a simulation source.

| Rule | Why |
| --- | --- |
| A class may implement several of the three interfaces (`BlueForcePatrolSource` is blue force + own pose). Register once per service; `TryAddSingleton` keeps **one shared instance**. | Two instances would each keep their own patrol clock, and the position reported over `OwnPose` would drift away from the same unit's blue force. |
| Guard every piece of mutable state a dual-role source keeps between cycles — counters, state machines, and above all `Random`. | Each runner is its own `BackgroundService` on its own timer, so both `Produce*Async` genuinely run at once; a shared `Random` returns garbage and can corrupt itself. `BlueForcePatrolSource` keeps all of its behind one gate and computes the rest as a pure function of options + time. Otherwise keep the source stateless. |
| A blue force source cannot **delete**. | No delete RPC. It disappears by stopping keep-alives and waiting out the implementation's timeout. |
| A blue force source cannot set `own_blue_force` / `associated_organization_unit_identity`. | `UpdateBlueForce` has no field for either — they belong to the answering system. Here: `Simulator:BlueForce:OwnIdentity` ([Configuration](CONFIGURATION.md#simulatorblueforce)). |

## Adding support for more situation object types

Implement `ISituationObjectMerger` for the `UpdateSituationObject` oneof case (template: `SymbolMerger`, ~40 lines) and register it:

```csharp
services.AddSingleton<ISituationObjectMerger, RouteMerger>();
```

The store discovers mergers by their `HandledCase`; nothing else changes.
