using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TacticalApi.Simulator.Core;

/// <summary>
///     Composes and runs an adapter executable: a plain generic
///     <see cref="Host" /> (no ASP.NET Core, no port ever bound - an adapter
///     has no web surface) wired with the gRPC ingest client
///     (<see cref="SimulatorCoreServiceCollectionExtensions.AddSituationIngestClient" />)
///     plus whichever <c>ISimulationSource</c>(s) the caller registers. Every
///     <c>TacticalApi.Simulator.Adapter.*</c> project's entire <c>Program.cs</c>
///     is a single call to this.
/// </summary>
public static class AdapterHost
{
    /// <summary>Builds, configures, and runs the adapter host; blocks until shutdown.</summary>
    public static void Run(string[] args, Action<IServiceCollection, IConfiguration> configureSources)
    {
        AppSettingsBootstrap.EnsureAppSettingsFile();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSituationIngestClient(builder.Configuration);
        configureSources(builder.Services, builder.Configuration);

        builder.Build().Run();
    }
}
