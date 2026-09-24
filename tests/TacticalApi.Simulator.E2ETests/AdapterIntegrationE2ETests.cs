using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;
// "Host" is ambiguous here between Microsoft.Extensions.Hosting.Host and the
// sibling TacticalApi.Simulator.Host namespace/assembly this test also
// references - alias it explicitly rather than fully-qualifying every call.
using GenericHost = Microsoft.Extensions.Hosting.Host;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     E2E coverage for the two-executable topology: a real Host (server) and
///     a real adapter, each its own DI container/host, talking only over a
///     real gRPC socket - exactly like running
///     <c>TacticalApi.Simulator.Adapter.Synthetic</c> against
///     <c>TacticalApi.Simulator.Host</c> as separate processes, just composed
///     directly in-test via the same shared Core building blocks
///     (<see cref="AdapterHost" />'s composition) instead of two `dotnet run`s.
/// </summary>
public sealed class AdapterIntegrationE2ETests
{
    [Fact]
    public async Task SyntheticScenarioAdapter_PopulatesAllElevenObjectTypes_EndToEnd()
    {
        // Real Kestrel socket required: the adapter's gRPC client needs a real
        // address to dial into, unlike the in-memory TestServer transport the
        // rest of the E2E suite uses.
        await using var serverFactory = new SimulatorFactory(null, useRealServer: true);
        var client = serverFactory.CreateGrpcClient();

        var adapterBuilder = GenericHost.CreateApplicationBuilder();
        adapterBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{GrpcIngestOptions.SectionName}:{nameof(GrpcIngestOptions.Address)}"] = "http://localhost:5100",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.Enabled)}"] = "true",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.UpdateInterval)}"] =
                "00:00:00.500",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.EventProbability)}"] = "1.0",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.ChatProbability)}"] = "1.0"
        });
        adapterBuilder.Services.AddSituationIngestClient(adapterBuilder.Configuration);
        adapterBuilder.Services.AddSyntheticSources(adapterBuilder.Configuration);

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var adapter = adapterBuilder.Build();
        await adapter.StartAsync(cts.Token);

        try
        {
            var expectedCases = Enum.GetValues<SituationObject.TypeOneofCase>()
                .Where(c => c != SituationObject.TypeOneofCase.None)
                .ToHashSet();

            while (!cts.IsCancellationRequested)
            {
                var get = await client.GetSituationObjectsAsync(
                    new GetSituationObjectsRequest(), cancellationToken: cts.Token);
                var presentCases = get.SituationObjects.Select(o => o.TypeCase).ToHashSet();
                if (expectedCases.IsSubsetOf(presentCases)) return; // every object type observed over gRPC

                await Task.Delay(250, cts.Token);
            }

            Assert.Fail("Adapter did not produce all 11 object types in time.");
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task ConvoyEscortAdapter_PopulatesRouteVehiclesAmbushAndSalute_EndToEnd()
    {
        await using var serverFactory = new SimulatorFactory(null, useRealServer: true);
        var client = serverFactory.CreateGrpcClient();
        var blueForceClient = serverFactory.CreateBlueForceClient();

        var adapterBuilder = GenericHost.CreateApplicationBuilder();
        adapterBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{GrpcIngestOptions.SectionName}:{nameof(GrpcIngestOptions.Address)}"] = "http://localhost:5100",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.Enabled)}"] = "false",
            [$"{SyntheticAirTrackOptions.SectionName}:{nameof(SyntheticAirTrackOptions.Enabled)}"] = "false",
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.Enabled)}"] = "false",
            [$"{ConvoyEscortOptions.SectionName}:{nameof(ConvoyEscortOptions.Enabled)}"] = "true",
            [$"{ConvoyEscortOptions.SectionName}:{nameof(ConvoyEscortOptions.UpdateInterval)}"] = "00:00:00.500",
            // Guaranteed ambush on the very first cycle, regardless of risk-zone position or seed.
            [$"{ConvoyEscortOptions.SectionName}:{nameof(ConvoyEscortOptions.BaseAmbushProbability)}"] = "1"
        });
        adapterBuilder.Services.AddSituationIngestClient(adapterBuilder.Configuration);
        adapterBuilder.Services.AddSyntheticSources(adapterBuilder.Configuration);

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var adapter = adapterBuilder.Build();
        await adapter.StartAsync(cts.Token);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var get = await client.GetSituationObjectsAsync(
                    new GetSituationObjectsRequest(), cancellationToken: cts.Token);

                // The serial itself now arrives over BlueForceTracking - the whole
                // point of the split - so the scenario is only fully observed when
                // both services have been fed from this one adapter.
                var blueForces = await blueForceClient.GetBlueForcesAsync(
                    new GetBlueForcesRequest(), cancellationToken: cts.Token);

                var hasRoute = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.Route &&
                    o.Route.Identity?.StringIdentity == "convoy:route:condor");
                var hasVehicles = blueForces.BlueForces.Any(bf =>
                    bf.Identity?.StringIdentity.StartsWith("convoy:vehicle:", StringComparison.Ordinal) == true);
                var hasAmbush = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.ActionEvent &&
                    o.ActionEvent.ActionEventType?.Content == ActionEventType.Ambush);
                var hasSalute = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.NatoMessageDocument &&
                    o.NatoMessageDocument.Identity?.StringIdentity == "convoy:nato:salute");

                if (hasRoute && hasVehicles && hasAmbush && hasSalute) return; // full scenario observed over gRPC

                await Task.Delay(250, cts.Token);
            }

            Assert.Fail("Adapter did not produce the convoy route, its vehicles as blue forces, an ambush, and a SALUTE report in time.");
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task CombatOutpostDefenseAdapter_PopulatesPerimeterOpsDefendTaskAndContact_EndToEnd()
    {
        await using var serverFactory = new SimulatorFactory(null, useRealServer: true);
        var client = serverFactory.CreateGrpcClient();
        var blueForceClient = serverFactory.CreateBlueForceClient();

        var adapterBuilder = GenericHost.CreateApplicationBuilder();
        adapterBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{GrpcIngestOptions.SectionName}:{nameof(GrpcIngestOptions.Address)}"] = "http://localhost:5100",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.Enabled)}"] = "false",
            [$"{SyntheticAirTrackOptions.SectionName}:{nameof(SyntheticAirTrackOptions.Enabled)}"] = "false",
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.Enabled)}"] = "false",
            [$"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.Enabled)}"] = "true",
            [$"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.UpdateInterval)}"] =
                "00:00:00.500",
            [$"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.ObservationPostCount)}"] =
                "3",
            // Guaranteed ground assault on the very first cycle, regardless of time of day or seed.
            [$"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.DayContactProbability)}"] =
                "1",
            [
                $"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.NightContactProbabilityMultiplier)}"
            ] = "1",
            [
                $"{CombatOutpostDefenseOptions.SectionName}:{nameof(CombatOutpostDefenseOptions.AssaultProbabilityGivenContact)}"
            ] = "1"
        });
        adapterBuilder.Services.AddSituationIngestClient(adapterBuilder.Configuration);
        adapterBuilder.Services.AddSyntheticSources(adapterBuilder.Configuration);

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var adapter = adapterBuilder.Build();
        await adapter.StartAsync(cts.Token);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var get = await client.GetSituationObjectsAsync(
                    new GetSituationObjectsRequest(), cancellationToken: cts.Token);

                // The perimeter graphic stays a situation object; the posts manning it
                // report themselves over BlueForceTracking.
                var blueForces = await blueForceClient.GetBlueForcesAsync(
                    new GetBlueForcesRequest(), cancellationToken: cts.Token);

                var hasPerimeter = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.Symbol &&
                    o.Symbol.Identity?.StringIdentity == "cop:perimeter");
                var opCount = blueForces.BlueForces.Count(bf =>
                    bf.Identity?.StringIdentity.StartsWith("cop:bf:op:", StringComparison.Ordinal) == true);
                var hasDefendTask = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.ActionTask &&
                    o.ActionTask.Identity?.StringIdentity == "cop:task:defend");
                var hasAssault = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.ActionEvent &&
                    o.ActionEvent.ActionEventType?.Content == ActionEventType.Ambush);
                var hasSitrep = get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.NatoMessageDocument &&
                    o.NatoMessageDocument.Identity?.StringIdentity == "cop:nato:sitrep");

                if (hasPerimeter && opCount == 3 && hasDefendTask && hasAssault && hasSitrep)
                    return; // full scenario observed over gRPC

                await Task.Delay(250, cts.Token);
            }

            Assert.Fail("Adapter did not produce the COP perimeter/OPs, defend task, a ground assault, and a SITREP in time.");
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task BlueForcePatrolAdapter_FeedsBothBlueForceTrackingAndOwnPose_EndToEnd()
    {
        // The topology check for the contract's other two services: one adapter
        // process, one channel, two runners driving one source - and both RPCs
        // landing on the Host over a real socket.
        await using var serverFactory = new SimulatorFactory(null, useRealServer: true);
        var blueForces = serverFactory.CreateBlueForceClient();
        var ownPose = serverFactory.CreateOwnPoseClient();

        var adapterBuilder = GenericHost.CreateApplicationBuilder();
        adapterBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{GrpcIngestOptions.SectionName}:{nameof(GrpcIngestOptions.Address)}"] = "http://localhost:5100",
            [$"{SyntheticScenarioOptions.SectionName}:{nameof(SyntheticScenarioOptions.Enabled)}"] = "false",
            [$"{SyntheticAirTrackOptions.SectionName}:{nameof(SyntheticAirTrackOptions.Enabled)}"] = "false",
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.Enabled)}"] = "true",
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.UpdateInterval)}"] =
                "00:00:00.500",
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.DismountCount)}"] = "2",
            // No GNSS dropout, so the own position is reported on every cycle and the
            // test isn't waiting on a coin flip.
            [$"{BlueForcePatrolOptions.SectionName}:{nameof(BlueForcePatrolOptions.GnssOutageProbability)}"] = "0"
        });
        adapterBuilder.Services.AddSituationIngestClient(adapterBuilder.Configuration);
        adapterBuilder.Services.AddSyntheticSources(adapterBuilder.Configuration);

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var adapter = adapterBuilder.Build();
        await adapter.StartAsync(cts.Token);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var forces = await blueForces.GetBlueForcesAsync(
                    new GetBlueForcesRequest(), cancellationToken: cts.Token);
                var position = await ownPose.GetPositionAsync(
                    new GetPositionRequest(), cancellationToken: cts.Token);

                // Carrier + UAS + leader + two riflemen, with the three type flags
                // spread across them, and a position under the configured source.
                var hasWholeSection = forces.BlueForces.Count == 5
                                      && forces.BlueForces.Any(bf => bf.BlueForceType?.IsLeader == true)
                                      && forces.BlueForces.Any(bf => bf.BlueForceType?.IsVehicle == true)
                                      && forces.BlueForces.Any(bf => bf.BlueForceType?.IsUnmanned == true);
                var hasPosition = position.Position?.SourceIdentifier == "GNSS";

                if (hasWholeSection && hasPosition) return; // both services fed over gRPC

                await Task.Delay(250, cts.Token);
            }

            Assert.Fail("Adapter did not feed BlueForceTracking and OwnPose in time.");
        }
        finally
        {
            await adapter.StopAsync();
        }
    }
}
