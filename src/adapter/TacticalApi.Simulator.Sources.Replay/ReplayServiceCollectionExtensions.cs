using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TacticalApi.Simulator.Sources.Replay;

/// <summary>DI registration for the situation recorder and the replay player.</summary>
public static class ReplayServiceCollectionExtensions
{
    /// <summary>
    ///     Registers both halves of record/replay. Each is independently gated by its
    ///     own <c>Enabled</c> flag, so one adapter executable covers both jobs: run it
    ///     with "Adapter:Recorder:Enabled" to capture, and again with
    ///     "Adapter:Replay:Enabled" to play back.
    /// </summary>
    public static IServiceCollection AddReplaySources(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RecorderOptions>()
            .Bind(configuration.GetSection(RecorderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ReplayOptions>()
            .Bind(configuration.GetSection(ReplayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<SituationRecorder>();
        services.AddHostedService<ReplayPlayer>();

        return services;
    }
}
