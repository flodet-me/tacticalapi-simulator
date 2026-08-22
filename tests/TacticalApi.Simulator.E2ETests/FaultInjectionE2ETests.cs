using System.Diagnostics;
using Grpc.Core;
using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for fault injection: a real host configured to misbehave,
///     driven through real gRPC calls
///     (src/simulator/TacticalApi.Simulator.Host/Faults/).
/// </summary>
public sealed class FaultInjectionE2ETests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ErrorHeaderFault_ReturnsAnUnsuccessfulHeaderWithoutFailingTheCall()
    {
        // Arrange - the failure clients most often miss: the RPC succeeds, so nothing
        // throws, and the error lives only in a field they never read.
        await using var factory = CreateFactory(("Simulator:Faults:ErrorHeaderProbability", "1.0"));
        var client = factory.CreateGrpcClient();

        // Act
        var response = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("fault"), T0, "ALPHA") }
        });

        // Assert
        Assert.False(response.Header.Success);
        Assert.Contains("Injected fault", response.Header.ErrorMessage, StringComparison.Ordinal);

        // And nothing was written.
        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.Empty(get.SituationObjects);
    }

    [Fact]
    public async Task DropWriteFault_AcknowledgesTheWriteAndDiscardsIt()
    {
        // Arrange - the server-lost-your-update case, detectable only by reconciling.
        await using var factory = CreateFactory(("Simulator:Faults:DropWriteProbability", "1.0"));
        var client = factory.CreateGrpcClient();

        // Act
        var response = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("dropped"), T0, "ALPHA") }
        });

        // Assert
        Assert.True(response.Header.Success);
        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.Empty(get.SituationObjects);
    }

    [Fact]
    public async Task RpcErrorFault_FailsTheCallItself()
    {
        // Arrange
        await using var factory = CreateFactory(
            ("Simulator:Faults:RpcErrorProbability", "1.0"),
            ("Simulator:Faults:RpcStatusCode", "Unavailable"));
        var client = factory.CreateGrpcClient();

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            client.GetSituationObjectsAsync(new GetSituationObjectsRequest()).ResponseAsync);

        // Assert
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
    }

    [Fact]
    public async Task LatencyFault_DelaysTheCall()
    {
        // Arrange
        await using var factory = CreateFactory(("Simulator:Faults:Latency", "00:00:00.300"));
        var client = factory.CreateGrpcClient();

        // Act
        var stopwatch = Stopwatch.StartNew();
        await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());

        // Assert
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250),
            $"the injected latency was not applied (call took {stopwatch.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public async Task StreamAbortFault_CutsAnIdleSubscriptionShort()
    {
        // Arrange - an idle stream is exactly where reconnect logic goes untested,
        // because in development nothing ever interrupts one.
        await using var factory = CreateFactory(("Simulator:Faults:StreamAbortAfter", "00:00:00.500"));
        var client = factory.CreateGrpcClient();

        using var call = client.SubscribeSituationObjectEvents(new SubscribeSituationObjectEventsRequest());

        // Act - nothing is ever written, so only the injected lifetime can end this.
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using var timeout = new CancellationTokenSource(E2E.Timeout);
            await foreach (var _ in call.ResponseStream.ReadAllAsync(timeout.Token))
            {
                // Drain; the stream is expected to fault rather than complete.
            }
        });

        // Assert
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
    }

    [Fact]
    public async Task FaultsAreInert_WhenTheMasterSwitchIsOff()
    {
        // Arrange - a stray probability left in a config file must not quietly
        // degrade a normal run.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:Faults:Enabled"] = "false",
            ["Simulator:Faults:ErrorHeaderProbability"] = "1.0",
            ["Simulator:Faults:RpcErrorProbability"] = "1.0"
        });
        var client = factory.CreateGrpcClient();

        // Act
        var response = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("inert"), T0, "ALPHA") }
        });

        // Assert
        Assert.True(response.Header.Success, response.Header.ErrorMessage);
    }

    private static SimulatorFactory CreateFactory(params (string Key, string Value)[] settings)
    {
        var configuration = new Dictionary<string, string?> { ["Simulator:Faults:Enabled"] = "true" };
        foreach (var (key, value) in settings) configuration[key] = value;

        return new SimulatorFactory(configuration);
    }

    private static string Unique(string prefix)
    {
        return $"e2e:{prefix}:{Guid.NewGuid():N}";
    }
}
