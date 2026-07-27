using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticalApi.Simulator.Core;

namespace TacticalApi.Simulator.Sources.Nws;

/// <summary>DI registration for the NWS active-alerts simulation source.</summary>
public static class NwsServiceCollectionExtensions
{
    /// <summary>Registers the named HttpClient, options, and the source itself.</summary>
    public static IServiceCollection AddNwsSources(this IServiceCollection services, IConfiguration configuration)
    {
        // api.weather.gov rejects requests without an identifying User-Agent.
        // AddStandardResilienceHandler adds retry with exponential backoff+jitter (honoring
        // Retry-After on 429s) plus a circuit breaker, instead of ProduceAsync failing outright
        // on the first transient error - this fires on every poll cycle forever otherwise.
        // HttpClient.Timeout wraps the whole pipeline including retries, so it's set to infinite
        // here - the resilience handler's own AttemptTimeout/TotalRequestTimeout (10s/30s by
        // default) are what actually bound a single ProduceAsync call now.
        services.AddHttpClient(NwsAlertSource.HttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "TacticalApiSimulator/1.0 (+https://github.com/Rheinmetall/tacticalapi)");
            })
            .AddStandardResilienceHandler();
        services.AddOptions<NwsOptions>()
            .Bind(configuration.GetSection(NwsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<NwsAlertSource>();

        return services;
    }
}
