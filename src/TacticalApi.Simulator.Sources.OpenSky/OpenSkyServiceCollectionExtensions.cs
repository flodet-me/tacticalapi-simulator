using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.OpenSky;

public static class OpenSkyServiceCollectionExtensions
{
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
