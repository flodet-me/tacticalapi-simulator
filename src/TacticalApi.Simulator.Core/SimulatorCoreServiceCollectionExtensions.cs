using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Merging;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Core.Store;

namespace TacticalApi.Simulator.Core;

/// <summary>DI registration helpers for the simulator's core services and simulation sources.</summary>
public static class SimulatorCoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers store, event broker, mergers, expiry sweep, and the gRPC
    ///     client sources use to submit writes (<see cref="GrpcIngestOptions" />,
    ///     bound from "Simulator:Ingest").
    /// </summary>
    public static IServiceCollection AddSimulatorCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SituationEventBroker>();
        services.AddSingleton<SituationStore>();

        // All 11 situation object types of the v0 contract are supported;
        // AllMergers is the single source of truth for the merger set.
        foreach (var merger in AllMergers.CreateAll()) services.AddSingleton<ISituationObjectMerger>(merger);

        services.AddHostedService<ExpirySweeper>();

        // The TacticalAPI contract is plain h2c (HTTP/2 without TLS, no security
        // features by design - see ARCHITECTURE.md); Grpc.Net.Client requires
        // this switch to call an h2c endpoint at all.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        services.AddOptions<GrpcIngestOptions>()
            .Bind(configuration.GetSection(GrpcIngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(sp =>
            GrpcChannel.ForAddress(sp.GetRequiredService<IOptionsMonitor<GrpcIngestOptions>>().CurrentValue.Address));
        services.AddSingleton(sp => new Situation.SituationClient(sp.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<ISituationIngest, GrpcSituationIngest>();

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
