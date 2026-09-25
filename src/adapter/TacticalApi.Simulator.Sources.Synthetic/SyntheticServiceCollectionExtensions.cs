using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>DI registration for the fully offline synthetic simulation sources.</summary>
public static class SyntheticServiceCollectionExtensions
{
    /// <summary>
    ///     Registers options and hosted runners for every offline source: air tracks, each
    ///     scenario, the load generator, and the blue force patrol that feeds the contract's
    ///     other two services.
    /// </summary>
    public static IServiceCollection AddSyntheticSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SyntheticAirTrackOptions>()
            .Bind(configuration.GetSection(SyntheticAirTrackOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<SyntheticAirTrackSource>();

        services.AddOptions<SyntheticScenarioOptions>()
            .Bind(configuration.GetSection(SyntheticScenarioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<SyntheticScenarioSource>();

        // The convoy feeds both services: the serial itself is a blue force picture,
        // everything it reports about (route, ambush, hostiles, CASEVAC, SALUTE) is a
        // situation picture. One shared instance, so the two agree on where the
        // convoy is - see AddSimulationSource.
        services.AddOptions<ConvoyEscortOptions>()
            .Bind(configuration.GetSection(ConvoyEscortOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<ConvoyEscortSource>();
        services.AddBlueForceSource<ConvoyEscortSource>();

        services.AddOptions<LoadGeneratorOptions>()
            .Bind(configuration.GetSection(LoadGeneratorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<LoadGeneratorSource>();

        // Likewise the COP: its garrison and observation posts are blue forces, its
        // perimeter graphic, orders, contacts and SITREP are situation objects.
        services.AddOptions<CombatOutpostDefenseOptions>()
            .Bind(configuration.GetSection(CombatOutpostDefenseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<CombatOutpostDefenseSource>();
        services.AddBlueForceSource<CombatOutpostDefenseSource>();

        // One source, two services: the same patrol feeds BlueForceTracking and
        // OwnPose, so it gets a runner for each and - thanks to TryAddSingleton in
        // AddBlueForceSource/AddOwnPoseSource - one shared instance driving both,
        // which is what keeps the leader's reported position and its blue force
        // position from drifting apart.
        services.AddOptions<BlueForcePatrolOptions>()
            .Bind(configuration.GetSection(BlueForcePatrolOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddBlueForceSource<BlueForcePatrolSource>();
        services.AddOwnPoseSource<BlueForcePatrolSource>();

        services.AddOptions<GeometryShowcaseOptions>()
            .Bind(configuration.GetSection(GeometryShowcaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<GeometryShowcaseSource>();

        services.AddOptions<EasternFlankOptions>()
            .Bind(configuration.GetSection(EasternFlankOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<EasternFlankSource>();

        return services;
    }
}
