using System.Net.Http.Json;
using System.Text.Json;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     E2E tests for the Host on its own: the expiry sweeper and the HTTP
///     status endpoint. Coverage that involves a data source feeding the
///     situation lives in <c>AdapterIntegrationE2ETests</c> instead, since
///     sources are a separate adapter executable now, not part of the Host.
/// </summary>
public sealed class HostedPipelineE2ETests
{
    [Fact]
    public async Task ExpirySweeper_MarksExpiredObjectsDeleted_EndToEnd()
    {
        using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            [$"{SimulatorOptions.SectionName}:{nameof(SimulatorOptions.ExpirySweepInterval)}"] = "00:00:01"
        });
        var client = factory.CreateGrpcClient();
        var id = $"e2e:expiring:{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        using var cts = new CancellationTokenSource(E2E.Timeout);

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, now, "SHORT-LIVED", expiry: now.AddSeconds(1)) }
        }, cancellationToken: cts.Token);

        while (!cts.IsCancellationRequested)
        {
            var get = await client.GetSituationObjectsAsync(
                new GetSituationObjectsRequest(), cancellationToken: cts.Token);
            if (!get.SituationObjects.Any(o =>
                    o.TypeCase == SituationObject.TypeOneofCase.Symbol &&
                    o.Symbol.Identity?.StringIdentity == id))
                return; // expired object no longer part of the snapshot

            await Task.Delay(500, cts.Token);
        }

        Assert.Fail("Expired object was not removed from the snapshot in time.");
    }

    [Fact]
    public async Task StatusEndpoint_ReportsObjectAndSubscriberCounts()
    {
        await using var factory = new SimulatorFactory();
        var grpc = factory.CreateGrpcClient();
        using var http = factory.CreateClient();

        await grpc.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol($"e2e:status:{Guid.NewGuid():N}", DateTimeOffset.UtcNow, "X") }
        });

        var status = await http.GetFromJsonAsync<JsonElement>("/");

        Assert.Equal("TacticalAPI Simulator", status.GetProperty("service").GetString());
        Assert.True(status.GetProperty("situationObjects").GetInt32() >= 1);
        Assert.True(status.GetProperty("subscribers").GetInt32() >= 0);
    }
}
