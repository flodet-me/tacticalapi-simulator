using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.Synthetic;

public static class SyntheticServiceCollectionExtensions
{
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
