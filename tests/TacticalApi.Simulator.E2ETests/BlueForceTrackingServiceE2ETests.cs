using Grpc.Core;
using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for <c>rheinmetall.tactical_api.v0.BlueForceTracking</c>
///     over real gRPC against the real Host (Program.cs, full DI, real interceptor
///     pipeline).
///     What these add over <c>BlueForceStoreTests</c> is everything between a client
///     and the store: that the service is actually mapped, that it is reachable over
///     gRPC-Web as well as native gRPC, and that the streaming contract - snapshot
///     first, then live changes, including the implicit deletion - holds across a
///     socket rather than just inside a broker.
/// </summary>
public sealed class BlueForceTrackingServiceE2ETests
{
    [Fact]
    public async Task AddOrUpdateBlueForces_ThenGetBlueForces_RoundTripsTheBlueForce()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateBlueForceClient();

        // Act
        var write = await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:1", DateTimeOffset.UtcNow, "ALPHA", 52.5, 13.4) }
        });

        var read = await client.GetBlueForcesAsync(new GetBlueForcesRequest());

        // Assert
        Assert.True(write.Header.Success);
        Assert.True(read.Header.Success);
        var stored = Assert.Single(read.BlueForces);
        Assert.Equal("ALPHA", stored.Callsign);
        Assert.Equal(52.5, stored.PointLocation.GeoPoint.LatitudeCoordinate);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task AddOrUpdateBlueForces_OverGrpcWeb_WorksTheSameWay()
    {
        // The transport the official Rheinmetall test client uses. A service mapped
        // without EnableGrpcWeb would pass every other test here and fail this one.
        await using var factory = new SimulatorFactory();
        var channel = Grpc.Net.Client.GrpcChannel.ForAddress(factory.Server.BaseAddress,
            new Grpc.Net.Client.GrpcChannelOptions
            {
                HttpHandler = new Grpc.Net.Client.Web.GrpcWebHandler(
                    Grpc.Net.Client.Web.GrpcWebMode.GrpcWeb, factory.Server.CreateHandler())
            });
        var client = new BlueForceTracking.BlueForceTrackingClient(channel);

        var write = await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:web", DateTimeOffset.UtcNow, "WEB") }
        });
        var read = await client.GetBlueForcesAsync(new GetBlueForcesRequest());

        Assert.True(write.Header.Success);
        Assert.Equal("WEB", Assert.Single(read.BlueForces).Callsign);
    }

    [Fact]
    public async Task AddOrUpdateBlueForces_MissingIdentity_IsRefusedWithAnExplainingHeader()
    {
        await using var factory = new SimulatorFactory();
        var client = factory.CreateBlueForceClient();

        var response = await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates =
            {
                new UpdateBlueForce
                {
                    LastContactTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
                        DateTimeOffset.UtcNow)
                }
            }
        });

        Assert.False(response.Header.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.Header.ErrorMessage));
    }

    [Fact]
    public async Task SubscribeBlueForceEvents_SendsExistingBlueForcesBeforeLiveChanges()
    {
        // "Initially, all existing blue forces are returned for every call."
        await using var factory = new SimulatorFactory();
        var client = factory.CreateBlueForceClient();

        await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:existing", DateTimeOffset.UtcNow, "EXISTING") }
        });

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var call = client.SubscribeBlueForceEvents(
            new SubscribeBlueForceEventsRequest(), cancellationToken: cts.Token);

        var seen = new List<string>();
        await foreach (var response in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            Assert.True(response.Header.Success);
            seen.AddRange(response.UpdatedBlueForces.Select(bf => bf.Callsign));

            if (seen.Contains("EXISTING", StringComparer.Ordinal))
            {
                // Only now does anything change, so what arrives next is by
                // definition a live event rather than part of the snapshot.
                await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
                {
                    BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:live", DateTimeOffset.UtcNow, "LIVE") }
                });
            }

            if (seen.Contains("LIVE", StringComparer.Ordinal)) break;
        }

        Assert.Equal(["EXISTING", "LIVE"], seen);
    }

    [Fact]
    public async Task SubscribeBlueForceEvents_AnnouncesTheImplicitDeletionOnKeepAliveTimeout()
    {
        // The one deletion this service has. A client that never sees it keeps a
        // stale friendly on its map indefinitely, so it has to reach the stream.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:BlueForce:KeepAliveTimeout"] = "00:00:01",
            ["Simulator:BlueForce:SweepInterval"] = "00:00:01"
        });
        var client = factory.CreateBlueForceClient();

        using var cts = new CancellationTokenSource(E2E.Timeout);
        using var call = client.SubscribeBlueForceEvents(
            new SubscribeBlueForceEventsRequest(), cancellationToken: cts.Token);

        await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:abandoned", DateTimeOffset.UtcNow, "ABANDONED") }
        }, cancellationToken: cts.Token);

        BlueForce? deleted = null;
        await foreach (var response in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            deleted = response.UpdatedBlueForces.FirstOrDefault(bf => bf.IsDeleted);
            if (deleted is not null) break;
        }

        Assert.NotNull(deleted);
        Assert.Equal("e2e:bf:abandoned", deleted.Identity.StringIdentity);

        // And it is gone from the snapshot, not merely flagged in it.
        var read = await client.GetBlueForcesAsync(new GetBlueForcesRequest(), cancellationToken: cts.Token);
        Assert.Empty(read.BlueForces);
    }

    [Fact]
    public async Task AddOrUpdateBlueForces_KeepsABlueForceAliveAcrossItsTimeout()
    {
        // The other half of the rule, and the half a client depends on every day:
        // reporting on cadence must keep the blue force present.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:BlueForce:KeepAliveTimeout"] = "00:00:02",
            ["Simulator:BlueForce:SweepInterval"] = "00:00:01"
        });
        var client = factory.CreateBlueForceClient();

        for (var i = 0; i < 6; i++)
        {
            await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
            {
                BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:kept", DateTimeOffset.UtcNow, "KEPT") }
            });
            await Task.Delay(TimeSpan.FromMilliseconds(600));
        }

        var read = await client.GetBlueForcesAsync(new GetBlueForcesRequest());
        Assert.Equal("KEPT", Assert.Single(read.BlueForces).Callsign);
    }

    [Fact]
    public async Task BlueForceWrites_AreRejectedWhileTheSimulatorIsPaused()
    {
        // Pause is enforced in the store, so it applies to every service alike -
        // a situation frozen while blue forces kept moving would not be frozen.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateBlueForceClient();
        var http = factory.CreateClient();

        await http.PostAsync("/api/control/pause", content: null);

        var response = await client.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce("e2e:bf:paused", DateTimeOffset.UtcNow, "PAUSED") }
        });

        Assert.False(response.Header.Success);
        Assert.Empty((await client.GetBlueForcesAsync(new GetBlueForcesRequest())).BlueForces);
    }
}
