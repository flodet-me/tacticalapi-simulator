using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.OpenSky;

/// <summary>DI registration for the OpenSky Network live flight simulation source.</summary>
public static class OpenSkyServiceCollectionExtensions
{
    /// <summary>Registers the named HttpClient, options, and the source itself.</summary>
    public static IServiceCollection AddOpenSkySources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(OpenSkySource.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(9));
        services.AddOptions<OpenSkyOptions>()
            .Bind(configuration.GetSection(OpenSkyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<OpenSkySource>();

        return services;
    }
}
