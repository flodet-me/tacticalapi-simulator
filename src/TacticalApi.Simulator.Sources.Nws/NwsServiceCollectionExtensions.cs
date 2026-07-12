using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.Nws;

public static class NwsServiceCollectionExtensions
{
    public static IServiceCollection AddNwsSources(this IServiceCollection services, IConfiguration configuration)
    {
        // api.weather.gov rejects requests without an identifying User-Agent.
        services.AddHttpClient(NwsAlertSource.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(9);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TacticalApiSimulator/1.0 (+https://github.com/Rheinmetall/tacticalapi)");
        });
        services.AddOptions<NwsOptions>()
            .Bind(configuration.GetSection(NwsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<NwsAlertSource>();

        return services;
    }
}
