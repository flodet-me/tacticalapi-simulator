using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Sources.Synthetic;
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
    public void AddSituationIngestClient_RegistersAnIngestForEveryServiceOfTheContract()
    {
        // One channel, three services: an adapter feeding blue forces or an own
        // position must be repointed by the same single Adapter:Ingest:Address as one
        // feeding situation objects, not by a setting of its own.
        var services = new ServiceCollection();
        services.AddSituationIngestClient(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GrpcBlueForceIngest>(provider.GetRequiredService<IBlueForceIngest>());
        Assert.IsType<GrpcOwnPoseIngest>(provider.GetRequiredService<IOwnPoseIngest>());
        Assert.Same(
            provider.GetRequiredService<Grpc.Net.Client.GrpcChannel>(),
            provider.GetRequiredService<Grpc.Net.Client.GrpcChannel>());
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

    [Fact]
    public void AddSituationServer_RegistersAStoreAndASweepForEveryServiceOfTheContract()
    {
        // Each service expires its own state on its own terms, so each gets its own
        // background sweep; a missing one is invisible until something stops
        // disappearing when it should.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SimulatorOptions>();
        services.AddSituationServer();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<BlueForceStore>());
        Assert.NotNull(provider.GetService<OwnPoseStore>());
        Assert.NotNull(provider.GetService<BlueForceEventBroker>());
        Assert.NotNull(provider.GetService<PositionEventBroker>());

        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hosted, s => s is BlueForceTimeoutSweeper);
        Assert.Contains(hosted, s => s is OwnPoseStalenessSweeper);
    }

    [Fact]
    public void AddSourceHelpers_GiveOneClassFeedingTwoServicesASingleSharedInstance()
    {
        // BlueForcePatrolSource is registered as both a blue force source and an own
        // pose source. Two instances would each keep their own patrol clock, and the
        // leader's reported position would drift away from its own blue force.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBlueForceIngest>(new FakeBlueForceIngest());
        services.AddSingleton<IOwnPoseIngest>(new FakeOwnPoseIngest());
        services.AddSingleton(TestHelpers.Options(new BlueForcePatrolOptions()));
        services.AddSingleton(TimeProvider.System);
        services.AddBlueForceSource<BlueForcePatrolSource>();
        services.AddOwnPoseSource<BlueForcePatrolSource>();

        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hosted, s => s is BlueForceSourceRunner<BlueForcePatrolSource>);
        Assert.Contains(hosted, s => s is OwnPoseSourceRunner<BlueForcePatrolSource>);
        Assert.Same(
            provider.GetRequiredService<BlueForcePatrolSource>(),
            provider.GetRequiredService<BlueForcePatrolSource>());
        Assert.Single(services, d => d.ServiceType == typeof(BlueForcePatrolSource));
    }

    private sealed class FakeBlueForceIngest : IBlueForceIngest
    {
        public Task<IngestResult> AddOrUpdateAsync(
            IReadOnlyList<UpdateBlueForce> updates, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IngestResult.Ok);
        }
    }

    private sealed class FakeOwnPoseIngest : IOwnPoseIngest
    {
        public Task<IngestResult> UpdatePositionAsync(
            UpdatePosition position, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IngestResult.Ok);
        }
    }
}
