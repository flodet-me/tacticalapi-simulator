using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Merging;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Core.Store;

namespace TacticalApi.Simulator.Core;

/// <summary>DI registration helpers for the simulator's core services and simulation sources.</summary>
public static class SimulatorCoreServiceCollectionExtensions
{
    /// <summary>Registers store, event broker, mergers and expiry sweep.</summary>
    public static IServiceCollection AddSimulatorCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SituationEventBroker>();
        services.AddSingleton<SituationStore>();
        services.AddSingleton<ISituationIngest>(sp => sp.GetRequiredService<SituationStore>());

        // All 11 situation object types of the v0 contract are supported;
        // AllMergers is the single source of truth for the merger set.
        foreach (var merger in AllMergers.CreateAll()) services.AddSingleton<ISituationObjectMerger>(merger);

        services.AddHostedService<ExpirySweeper>();
        return services;
    }

    /// <summary>
    ///     Registers a simulation source together with its dedicated runner.
    /// </summary>
    public static IServiceCollection AddSimulationSource<TSource>(this IServiceCollection services)
        where TSource : class, ISimulationSource
    {
        services.AddSingleton<TSource>();
        services.AddHostedService<SimulationSourceRunner<TSource>>();
        return services;
    }
}
