using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.Synthetic;

/// <summary>DI registration for the fully offline synthetic simulation sources.</summary>
public static class SyntheticServiceCollectionExtensions
{
    /// <summary>Registers options and hosted runners for both the air-track and scenario sources.</summary>
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

        return services;
    }
}
