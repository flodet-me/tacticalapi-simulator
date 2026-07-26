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
        await using var serverFactory = new SimulatorFactory(useRealServer: true);
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
}
