using Grpc.Core;
using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for <c>rheinmetall.tactical_api.v0.OwnPose</c> over real gRPC
///     against the real Host.
///     The behaviour worth getting in front of a socket is the expiry one: a client
///     subscribed to its own position and receiving nothing has to be told that the
///     fix it is holding has stopped being current, and that message is produced by a
///     background sweep rather than by anything the client did.
/// </summary>
public sealed class OwnPoseServiceE2ETests
{
    [Fact]
    public async Task GetPosition_BeforeAnySourceReports_SucceedsWithNoPosition()
    {
        // A successful header with an empty position is the honest answer; failing
        // the call would claim the request was bad.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateOwnPoseClient();

        var response = await client.GetPositionAsync(new GetPositionRequest());

        Assert.True(response.Header.Success);
        Assert.Null(response.Position);
    }

    [Fact]
    public async Task UpdatePosition_ThenGetPosition_RoundTripsTheFix()
    {
        await using var factory = new SimulatorFactory();
        var client = factory.CreateOwnPoseClient();

        var write = await client.UpdatePositionAsync(E2E.Position("GNSS", 48.137, 11.575));
        var read = await client.GetPositionAsync(new GetPositionRequest());

        Assert.True(write.Header.Success);
        Assert.Equal("GNSS", read.Position.SourceIdentifier);
        Assert.Equal(48.137, read.Position.PointLocation.GeoPoint.LatitudeCoordinate);
        Assert.False(read.Position.IsInvalidOrExpired);
    }

    [Fact]
    public async Task UpdatePosition_MissingSourceIdentifier_IsRefused()
    {
        await using var factory = new SimulatorFactory();
        var client = factory.CreateOwnPoseClient();

        var response = await client.UpdatePositionAsync(new UpdatePositionRequest
        {
            Position = new UpdatePosition()
        });

        Assert.False(response.Header.Success);
        Assert.Contains("source_identifier", response.Header.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPosition_ReturnsTheConfiguredPrimarySourceRatherThanTheNewest()
    {
        // "The returned position is the one selected as primary position source by
        // the application" - here, by configuration.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:OwnPose:PrimarySource"] = "GNSS"
        });
        var client = factory.CreateOwnPoseClient();

        await client.UpdatePositionAsync(E2E.Position("GNSS", 48.1, 11.5));
        await client.UpdatePositionAsync(E2E.Position("DEAD-RECKONING", 49.9, 12.9));

        var read = await client.GetPositionAsync(new GetPositionRequest());
        Assert.Equal("GNSS", read.Position.SourceIdentifier);
        Assert.Equal(48.1, read.Position.PointLocation.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public async Task SubscribePositionChangedEvents_SendsTheCurrentPositionThenLiveChanges()
    {
        await using var factory = new SimulatorFactory();
        var client = factory.CreateOwnPoseClient();

        await client.UpdatePositionAsync(E2E.Position("GNSS", 48.1, 11.5));

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var call = client.SubscribePositionChangedEvents(
            new SubscribePositionEventsRequest(), cancellationToken: cts.Token);

        var latitudes = new List<double>();
        await foreach (var response in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            Assert.True(response.Header.Success);
            latitudes.Add(response.Position.PointLocation.GeoPoint.LatitudeCoordinate);

            if (latitudes.Count == 1) await client.UpdatePositionAsync(E2E.Position("GNSS", 48.2, 11.6));
            if (latitudes.Count == 2) break;
        }

        Assert.Equal([48.1, 48.2], latitudes);
    }

    [Fact]
    public async Task SubscribePositionChangedEvents_AnnouncesExpiryWithoutAnyFurtherUpdate()
    {
        // The case the contract describes ("the user entered a building"): the fix
        // keeps its coordinates and gains the flag, and a subscriber who is sending
        // nothing still finds out.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:OwnPose:PositionTimeout"] = "00:00:01",
            ["Simulator:OwnPose:SweepInterval"] = "00:00:01"
        });
        var client = factory.CreateOwnPoseClient();

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var call = client.SubscribePositionChangedEvents(
            new SubscribePositionEventsRequest(), cancellationToken: cts.Token);

        await client.UpdatePositionAsync(E2E.Position("GNSS", 48.137, 11.575), cancellationToken: cts.Token);

        Position? expired = null;
        await foreach (var response in call.ResponseStream.ReadAllAsync(cts.Token))
            if (response.Position.IsInvalidOrExpired)
            {
                expired = response.Position;
                break;
            }

        Assert.NotNull(expired);
        Assert.Equal("GNSS", expired.SourceIdentifier);

        // Kept, not dropped: "the old position continues to be used but is
        // considered outdated".
        Assert.Equal(48.137, expired.PointLocation.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public async Task PositionWrites_AreRejectedWhileTheSimulatorIsPaused()
    {
        await using var factory = new SimulatorFactory();
        var client = factory.CreateOwnPoseClient();
        var http = factory.CreateClient();

        await http.PostAsync("/api/control/pause", content: null);

        var response = await client.UpdatePositionAsync(E2E.Position("GNSS", 48.1, 11.5));

        Assert.False(response.Header.Success);
        Assert.Null((await client.GetPositionAsync(new GetPositionRequest())).Position);
    }
}
