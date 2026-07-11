using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Sources.OpenSky;
using TacticalApi.Simulator.Sources.Synthetic;

namespace TacticalApi.Simulator.Sources;

public static class SourcesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bundled example sources with options bound through
    /// IOptionsMonitor (validated, reloadable at runtime).
    /// </summary>
    public static IServiceCollection AddBundledSimulationSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SyntheticAirTrackOptions>()
            .Bind(configuration.GetSection(SyntheticAirTrackOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<SyntheticAirTrackSource>();

        services.AddHttpClient(OpenSkySource.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddOptions<OpenSkyOptions>()
            .Bind(configuration.GetSection(OpenSkyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<OpenSkySource>();

        return services;
    }
}
