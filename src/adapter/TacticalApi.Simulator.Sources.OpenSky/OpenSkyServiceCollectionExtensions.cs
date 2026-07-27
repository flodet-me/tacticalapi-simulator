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
        // OpenSky's anonymous tier is aggressively rate-limited (see OpenSkyOptions.PollInterval),
        // and this fires on every poll cycle forever - AddStandardResilienceHandler adds retry
        // with exponential backoff+jitter (honoring Retry-After on 429s) plus a circuit breaker,
        // instead of ProduceAsync failing outright on the first transient error or 429.
        // HttpClient.Timeout wraps the whole pipeline including retries, so it's set to
        // infinite here - the resilience handler's own AttemptTimeout/TotalRequestTimeout
        // (10s/30s by default) are what actually bound a single ProduceAsync call now.
        services.AddHttpClient(OpenSkySource.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler();
        services.AddOptions<OpenSkyOptions>()
            .Bind(configuration.GetSection(OpenSkyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSimulationSource<OpenSkySource>();

        return services;
    }
}
