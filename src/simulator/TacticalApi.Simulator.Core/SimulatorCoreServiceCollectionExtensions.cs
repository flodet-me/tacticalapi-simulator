using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Control;
using TacticalApi.Simulator.Core.Diagnostics;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Merging;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Core.Store;

namespace TacticalApi.Simulator.Core;

/// <summary>DI registration helpers for the simulator's core services and simulation sources.</summary>
public static class SimulatorCoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers what every adapter executable needs: a
    ///     <see cref="TimeProvider" />, and the gRPC clients sources submit writes
    ///     through - one per service of the contract, all on the same channel
    ///     (<see cref="GrpcIngestOptions" />, bound from "Adapter:Ingest"). See
    ///     <see cref="AdapterHost.Run" />.
    /// </summary>
    public static IServiceCollection AddSituationIngestClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

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

        // The other two services of the contract, over the same channel: an adapter
        // that feeds blue forces or an own position is talking to the same endpoint
        // as one that feeds situation objects, so Adapter:Ingest:Address stays the
        // single setting that repoints a whole adapter at another implementation.
        services.AddSingleton(sp =>
            new BlueForceTracking.BlueForceTrackingClient(sp.GetRequiredService<GrpcChannel>()));
        services.AddSingleton(sp => new OwnPose.OwnPoseClient(sp.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IBlueForceIngest, GrpcBlueForceIngest>();
        services.AddSingleton<IOwnPoseIngest, GrpcOwnPoseIngest>();

        // Recording decorates the ingest client instead of replacing it, so what
        // gets recorded is exactly what goes on the wire (see RecordingOptions).
        services.AddOptions<RecordingOptions>()
            .Bind(configuration.GetSection(RecordingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<GrpcSituationIngest>();
        services.AddSingleton<ISituationIngest>(sp =>
        {
            var inner = sp.GetRequiredService<GrpcSituationIngest>();
            if (!sp.GetRequiredService<IOptions<RecordingOptions>>().Value.Enabled) return inner;

            // Returned from a factory, so the container still owns the instance and
            // disposes it on shutdown - which is what closes the recording file.
            return ActivatorUtilities.CreateInstance<RecordingSituationIngest>(sp, inner);
        });

        return services;
    }

    /// <summary>
    ///     Registers the simulated server behind all three services of the
    ///     contract: the situation store with its mergers and expiry sweep, the blue
    ///     force store with its keep-alive timeout sweep, and the own-pose store with
    ///     its staleness sweep, plus one event broker each. Used only by
    ///     <c>TacticalApi.Simulator.Host</c>, which runs the actual gRPC services
    ///     against these stores - adapters never reference this, they only ever talk
    ///     to them over gRPC.
    /// </summary>
    public static IServiceCollection AddSituationServer(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SimulatorMetrics>();
        services.AddSingleton<SimulationPause>();
        services.AddSingleton<SituationEventBroker>();
        services.AddSingleton<SituationStore>();
        services.AddSingleton<BlueForceEventBroker>();
        services.AddSingleton<BlueForceStore>();
        services.AddSingleton<PositionEventBroker>();
        services.AddSingleton<OwnPoseStore>();

        // All 11 situation object types of the v0 contract are supported;
        // AllMergers is the single source of truth for the merger set.
        foreach (var merger in AllMergers.CreateAll()) services.AddSingleton<ISituationObjectMerger>(merger);

        services.AddHostedService<ExpirySweeper>();

        // Each service of the contract expires its own state on its own terms:
        // situation objects on expiry_time, blue forces on a missed keep-alive, the
        // own position by going stale in place rather than disappearing.
        services.AddHostedService<BlueForceTimeoutSweeper>();
        services.AddHostedService<OwnPoseStalenessSweeper>();

        return services;
    }

    /// <summary>
    ///     Registers a simulation source together with its dedicated runner.
    ///     The source itself is registered with TryAdd so one class can feed more
    ///     than one service of the contract - calling this alongside
    ///     <see cref="AddBlueForceSource{TSource}" /> gives both runners the same
    ///     instance rather than two copies with diverging state.
    /// </summary>
    public static IServiceCollection AddSimulationSource<TSource>(this IServiceCollection services)
        where TSource : class, ISimulationSource
    {
        services.TryAddSingleton<TSource>();
        services.AddHostedService<SimulationSourceRunner<TSource>>();
        return services;
    }

    /// <summary>
    ///     Registers a blue force source together with its dedicated runner; see
    ///     <see cref="AddSimulationSource{TSource}" />.
    /// </summary>
    public static IServiceCollection AddBlueForceSource<TSource>(this IServiceCollection services)
        where TSource : class, IBlueForceSource
    {
        services.TryAddSingleton<TSource>();
        services.AddHostedService<BlueForceSourceRunner<TSource>>();
        return services;
    }

    /// <summary>
    ///     Registers an own-position source together with its dedicated runner; see
    ///     <see cref="AddSimulationSource{TSource}" />.
    /// </summary>
    public static IServiceCollection AddOwnPoseSource<TSource>(this IServiceCollection services)
        where TSource : class, IOwnPoseSource
    {
        services.TryAddSingleton<TSource>();
        services.AddHostedService<OwnPoseSourceRunner<TSource>>();
        return services;
    }
}
