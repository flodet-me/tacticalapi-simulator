# Extending the simulator

Adding a data source or an object type is what this document is about. Two nearby things it is *not* about:

- Anything driven by the protobuf descriptors (`SituationObjectToUpdate`, `ReplayTimestamps`) needs no work when a new object type appears — that is why they are written that way. See [Architecture](ARCHITECTURE.md#interface-semantics-implemented). They cover the `Situation` service only; recordings are `Situation` traffic, deliberately.
- A new Host-side capability that isn't part of the contract (another fault, another control endpoint) belongs beside the existing ones in `Host/Faults/` or `Host/Control/`, never as a new RPC on the `Situation` service.

## Adding your own data source (e.g. an AIS ship tracker)

[`Sources.OpenSky`](../src/adapter/TacticalApi.Simulator.Sources.OpenSky/README.md) is a working example of exactly this pattern (a live, HTTP-polling, single-object-type source) — read its README alongside this section. If your API naturally produces more than one kind of situation object (text, a location, a warning area, ...) from a single feed, [`Sources.Nws`](../src/adapter/TacticalApi.Simulator.Sources.Nws/README.md) is the example to look at instead — `ProduceAsync` just returns a mixed batch of `UpdateSituationObject`s built by hand alongside `TrackUpdateFactory.CreateSymbolUpdate(...)`, there's no special multi-type mechanism required.

1. Implement `ISimulationSource` — fetch your data and map it to `UpdateSituationObject`. For track-like data, `TrackReport` + `TrackUpdateFactory.CreateSymbolUpdate(...)` does the TacticalAPI mapping for you:

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

2. Register it with its options, in a `services.AddXyzSources(configuration)` extension method
   (see `OpenSkyServiceCollectionExtensions.AddOpenSkySources` for the template — it's a few lines):

```csharp
public static IServiceCollection AddAisSources(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<AisOptions>().Bind(configuration.GetSection(AisOptions.SectionName))
        .ValidateDataAnnotations().ValidateOnStart();
    services.AddSimulationSource<AisShipSource>();
    return services;
}
```

3. Give it its own adapter executable — a new `TacticalApi.Simulator.Adapter.Ais` project under
   `src/adapter/` (plain `Microsoft.NET.Sdk`, `<OutputType>Exe</OutputType>`), referencing only `Core`
   (`..\..\simulator\TacticalApi.Simulator.Core\...` - `Core` lives under `src/simulator/`, not
   `src/adapter/`) and your new `Sources.Ais` project (a sibling under `src/adapter/`). Its entire
   `Program.cs`:

```csharp
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.Ais;

AdapterHost.Run(args, (services, configuration) => services.AddAisSources(configuration));
```

Plus its own `appsettings.json` with `Adapter:Ingest:Address` (defaults to the Host's own endpoint)
and `Adapter:Ais`, included both as `Content` (so it's copied next to the built executable) and as
`<EmbeddedResource Include="appsettings.json"/>` (so `AppSettingsBootstrap` can regenerate it from that
exact file if it's ever missing at runtime - see [Configuration](CONFIGURATION.md)). See
[`Adapter.OpenSky`](../src/adapter/TacticalApi.Simulator.Adapter.OpenSky) for a working example of this exact
shape, and add the new project to `TacticalApi.Simulator.slnx`.

Each source gets its own `SimulationSourceRunner` background service (so a slow or failing source
never stalls others in the same adapter; exceptions are logged and retried next cycle) and, per the
pattern above, its own adapter process entirely - it never touches the Host, which has no sources of
its own (see [Architecture](ARCHITECTURE.md)).

## Adding a blue force or own-position source

`BlueForceTracking` and `OwnPose` are separate services with their own write semantics, so they have their own
source interfaces — `IBlueForceSource` (produces `UpdateBlueForce`s) and `IOwnPoseSource` (produces one
`UpdatePosition`, or null to report nothing). Scheduling, enable/disable and failure handling are inherited from
the same `SourceRunner<T>` as an `ISimulationSource`, so a source looks the same apart from what it returns:

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

Register it with `services.AddBlueForceSource<MyTrackerSource>()` (or `AddOwnPoseSource<...>`), exactly as for a
simulation source.

One class may implement more than one of the three interfaces — `BlueForcePatrolSource` is both an
`IBlueForceSource` and an `IOwnPoseSource`. Register it once per service; the source itself is registered with
`TryAddSingleton`, so both runners drive **one shared instance** rather than two copies with diverging state.
That matters more than it looks: two instances would each keep their own patrol clock, and the position the
source reports over `OwnPose` would drift away from the same unit's blue force.

The price of that shared instance is that **its mutable state is touched by two threads**. Each runner is its own
`BackgroundService` on its own timer, so the two `Produce*Async` methods genuinely do run at once. Anything a
dual-role source keeps between cycles — a counter, a state machine, and above all a `Random`, which returns
garbage and can corrupt itself when used concurrently — has to be guarded. `BlueForcePatrolSource` keeps all of
its behind one gate and computes everything else as a pure function of the options and the current time; do the
same, or keep the source stateless.

Two things a blue force source cannot do, both by contract rather than by omission:

- **Delete.** There is no delete RPC. A blue force disappears only by stopping its keep-alives and waiting out
  the implementation's timeout.
- **Set `own_blue_force` or `associated_organization_unit_identity`.** `UpdateBlueForce` has no field for
  either — they belong to the system answering, not to the report. On this Host, the first comes from
  `Simulator:BlueForce:OwnIdentity` (see [Configuration](CONFIGURATION.md#simulatorblueforce)).

## Adding support for more situation object types

Implement `ISituationObjectMerger` for the `UpdateSituationObject` oneof case (see `SymbolMerger` as the template — it's ~40 lines) and register it:

```csharp
services.AddSingleton<ISituationObjectMerger, RouteMerger>();
```

The store discovers mergers by their `HandledCase`; no other change needed.
