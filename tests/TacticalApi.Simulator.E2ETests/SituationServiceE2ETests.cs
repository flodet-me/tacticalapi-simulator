using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests running the real host and talking to it through actual
///     gRPC calls (in-memory transport, full ASP.NET Core pipeline).
/// </summary>
public sealed class SituationServiceE2ETests(SimulatorFactory factory) : IClassFixture<SimulatorFactory>
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    [Fact]
    public async Task AddOrUpdate_ThenGet_RoundTripsSymbol()
    {
        var client = factory.CreateGrpcClient();
        var id = Unique("roundtrip");

        var addResponse = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0, "ALPHA", 53.0, 8.8) }
        });
        Assert.True(addResponse.Header.Success, addResponse.Header.ErrorMessage);

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        var obj = Assert.Single(get.SituationObjects, o => Matches(o, id));
        Assert.Equal("ALPHA", obj.Symbol.Name.Content);
        Assert.Equal(53.0, obj.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
        Assert.Equal("E2E", obj.Symbol.Name.CreationMetaData.CreatorIdentity.StringIdentity);
    }

    [Fact]
    public async Task AddOrUpdate_PartialUpdate_MergesOverTheWire()
    {
        var client = factory.CreateGrpcClient();
        var id = Unique("merge");

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0, "BRAVO", 53.0, 8.8) }
        });

        // Move the object without touching the name.
        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0.AddSeconds(1), latitude: 54.0, longitude: 9.0) }
        });

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        var obj = Assert.Single(get.SituationObjects, o => Matches(o, id));
        Assert.Equal("BRAVO", obj.Symbol.Name.Content);
        Assert.Equal(54.0, obj.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public async Task Delete_RemovesObjectFromSnapshot()
    {
        var client = factory.CreateGrpcClient();
        var id = Unique("delete");

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0, "CHARLIE") }
        });

        var deleteResponse = await client.DeleteSituationObjectsAsync(new DeleteSituationObjectsRequest
        {
            SituationObjects = { E2E.Delete(id, T0.AddSeconds(1)) }
        });
        Assert.True(deleteResponse.Header.Success);

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.DoesNotContain(get.SituationObjects, o => Matches(o, id));
    }

    [Fact]
    public async Task AddOrUpdate_InvalidUpdate_ReturnsErrorHeader()
    {
        var client = factory.CreateGrpcClient();

        var response = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { new UpdateSituationObject() } // oneof not set
        });

        Assert.False(response.Header.Success);
        Assert.False(string.IsNullOrEmpty(response.Header.ErrorMessage));
    }

    [Fact]
    public async Task Subscribe_DeliversSnapshotThenLiveEvents()
    {
        var client = factory.CreateGrpcClient();
        var snapshotId = Unique("sub-snapshot");
        var liveId = Unique("sub-live");
        using var cts = new CancellationTokenSource(E2E.Timeout);

        // One object BEFORE subscribing -> must arrive via the snapshot.
        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(snapshotId, T0, "SNAP") }
        }, cancellationToken: cts.Token);

        using var call = client.SubscribeSituationObjectEvents(
            new SubscribeSituationObjectEventsRequest(), cancellationToken: cts.Token);

        var seen = new HashSet<string>();
        var sentLive = false;

        while (await call.ResponseStream.MoveNext(cts.Token))
        {
            Assert.True(call.ResponseStream.Current.Header.Success);
            foreach (var obj in call.ResponseStream.Current.SituationObjects)
                seen.Add(obj.Symbol?.Identity?.StringIdentity ?? string.Empty);

            if (seen.Contains(snapshotId) && !sentLive)
            {
                // Snapshot arrived -> now trigger a live event.
                sentLive = true;
                await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
                {
                    SituationObjects = { E2E.Symbol(liveId, T0.AddSeconds(2), "LIVE") }
                }, cancellationToken: cts.Token);
            }

            if (seen.Contains(snapshotId) && seen.Contains(liveId)) return; // both snapshot and live delivery proven
        }

        Assert.Fail("Stream ended before snapshot and live event were received.");
    }

    [Fact]
    public async Task GrpcWeb_Transport_WorksLikeTheOfficialTestClient()
    {
        var client = factory.CreateGrpcWebClient();
        var id = Unique("grpcweb");

        var addResponse = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0, "WEB") }
        });
        Assert.True(addResponse.Header.Success);

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.Contains(get.SituationObjects, o => Matches(o, id));
    }

    [Fact]
    public async Task StaleUpdate_IsIgnoredOverTheWire()
    {
        var client = factory.CreateGrpcClient();
        var id = Unique("stale");

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0.AddMinutes(1), "NEW") }
        });
        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(id, T0, "OLD") }
        });

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        var obj = Assert.Single(get.SituationObjects, o => Matches(o, id));
        Assert.Equal("NEW", obj.Symbol.Name.Content);
    }

    private static bool Matches(SituationObject obj, string id)
    {
        return obj.TypeCase == SituationObject.TypeOneofCase.Symbol && obj.Symbol.Identity?.StringIdentity == id;
    }

    private static string Unique(string prefix)
    {
        return $"e2e:{prefix}:{Guid.NewGuid():N}";
    }
}
