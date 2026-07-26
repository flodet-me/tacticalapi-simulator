using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Store;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for Core's two DI composition entry points - one per
///     executable kind: <see cref="SimulatorCoreServiceCollectionExtensions.AddSituationIngestClient" />
///     (every adapter, via <see cref="AdapterHost" />) and
///     <see cref="SimulatorCoreServiceCollectionExtensions.AddSituationServer" />
///     (the Host only). Doesn't exercise actual gRPC/port behavior - that's
///     covered by the E2E layer - just that each registers the services it
///     promises and nothing from the other.
/// </summary>
public sealed class SimulatorCoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSituationIngestClient_RegistersGrpcIngestButNoStore()
    {
        var services = new ServiceCollection();
        services.AddSituationIngestClient(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GrpcSituationIngest>(provider.GetRequiredService<ISituationIngest>());
        Assert.Null(provider.GetService<SituationStore>());
    }

    [Fact]
    public void AddSituationServer_RegistersStoreAndExpirySweeper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SimulatorOptions>();
        services.AddSituationServer();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<SituationStore>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is ExpirySweeper);
        Assert.Null(provider.GetService<ISituationIngest>());
    }
}
