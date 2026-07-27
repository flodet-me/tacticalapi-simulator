# Extending the simulator

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

## Adding support for more situation object types

Implement `ISituationObjectMerger` for the `UpdateSituationObject` oneof case (see `SymbolMerger` as the template — it's ~40 lines) and register it:

```csharp
services.AddSingleton<ISituationObjectMerger, RouteMerger>();
```

The store discovers mergers by their `HandledCase`; no other change needed.
