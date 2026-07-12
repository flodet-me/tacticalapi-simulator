# Extending the simulator

## Adding your own data source (e.g. an AIS ship tracker)

[`Sources.OpenSky`](../src/TacticalApi.Simulator.Sources.OpenSky/README.md) is a working example of exactly this pattern (a live, HTTP-polling, single-object-type source) — read its README alongside this section. If your API naturally produces more than one kind of situation object (text, a location, a warning area, ...) from a single feed, [`Sources.Nws`](../src/TacticalApi.Simulator.Sources.Nws/README.md) is the example to look at instead — `ProduceAsync` just returns a mixed batch of `UpdateSituationObject`s built by hand alongside `TrackUpdateFactory.CreateSymbolUpdate(...)`, there's no special multi-type mechanism required.

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

2. Register it with its options:

```csharp
services.AddOptions<AisOptions>().Bind(config.GetSection("Simulator:Sources:Ais"))
    .ValidateDataAnnotations().ValidateOnStart();
services.AddSimulationSource<AisShipSource>();
```

Each source gets its own `SimulationSourceRunner` background service, so a slow or failing source never stalls the others; exceptions are logged and retried next cycle.

## Adding support for more situation object types

Implement `ISituationObjectMerger` for the `UpdateSituationObject` oneof case (see `SymbolMerger` as the template — it's ~40 lines) and register it:

```csharp
services.AddSingleton<ISituationObjectMerger, RouteMerger>();
```

The store discovers mergers by their `HandledCase`; no other change needed.
