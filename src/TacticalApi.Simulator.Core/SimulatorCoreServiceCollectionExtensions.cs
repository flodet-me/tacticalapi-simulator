using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Merging;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Core.Store;

namespace TacticalApi.Simulator.Core;

public static class SimulatorCoreServiceCollectionExtensions
{
    /// <summary>Registers store, event broker, mergers and expiry sweep.</summary>
    public static IServiceCollection AddSimulatorCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SituationEventBroker>();
        services.AddSingleton<SituationStore>();
        services.AddSingleton<ISituationIngest>(sp => sp.GetRequiredService<SituationStore>());

        // Supported situation object types - add more mergers here (or from
        // your own assembly) to extend the simulator.
        services.AddSingleton<ISituationObjectMerger, SymbolMerger>();
        services.AddSingleton<ISituationObjectMerger, TextDocumentMerger>();

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
