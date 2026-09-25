using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for the control endpoints: the real host, driven over HTTP
///     exactly as an operator or a test script would drive it
///     (src/simulator/TacticalApi.Simulator.Host/Control/ControlEndpoints.cs).
/// </summary>
public sealed class ControlEndpointsE2ETests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Reset_EmptiesTheSituation()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("reset"), T0, "ALPHA") }
        });

        // Act
        var response = await http.PostAsync(new Uri("/api/control/reset", UriKind.Relative), null);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, await ReadIntAsync(response, "dropped"));

        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.Empty(get.SituationObjects);
    }

    [Fact]
    public async Task Reset_AlsoDropsBlueForcesAndPositionSources()
    {
        // A reset returns the whole simulator to its just-started state. A situation
        // emptied while blue forces kept reporting from the previous run would be a
        // state no restart produces, and therefore not worth being able to reach.
        await using var factory = new SimulatorFactory();
        var blueForces = factory.CreateBlueForceClient();
        var ownPose = factory.CreateOwnPoseClient();
        var http = factory.CreateClient();

        await blueForces.AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce(Unique("reset-bf"), T0, "ALPHA") }
        });
        await ownPose.UpdatePositionAsync(E2E.Position("GNSS", 48.1, 11.5));

        // Act
        var response = await http.PostAsync(new Uri("/api/control/reset", UriKind.Relative), null);

        // Assert
        response.EnsureSuccessStatusCode();
        var dropped = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, dropped.GetProperty("droppedBlueForces").GetInt32());
        Assert.Equal(1, dropped.GetProperty("droppedPositionSources").GetInt32());
        Assert.Empty((await blueForces.GetBlueForcesAsync(new GetBlueForcesRequest())).BlueForces);
        Assert.Null((await ownPose.GetPositionAsync(new GetPositionRequest())).Position);
    }

    [Fact]
    public async Task State_ReportsEveryServiceOfTheContract()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var http = factory.CreateClient();

        await factory.CreateBlueForceClient().AddOrUpdateBlueForcesAsync(new AddOrUpdateBlueForcesRequest
        {
            BlueForcesToUpdates = { E2E.BlueForce(Unique("state-bf"), T0, "ALPHA") }
        });
        await factory.CreateOwnPoseClient().UpdatePositionAsync(E2E.Position("GNSS", 48.137, 11.575));

        // Act
        var state = await http.GetFromJsonAsync<JsonElement>("/api/control/state");

        // Assert
        Assert.Equal(1, state.GetProperty("blueForces").GetInt32());
        Assert.Equal(1, state.GetProperty("positionSources").GetInt32());
        var position = state.GetProperty("primaryPosition");
        Assert.Equal("GNSS", position.GetProperty("source").GetString());
        Assert.Equal(48.137, position.GetProperty("lat").GetDouble());
        Assert.False(position.GetProperty("invalidOrExpired").GetBoolean());
    }

    [Fact]
    public async Task BlueForcesAndPosition_CanBeInjectedByHand()
    {
        // Same shortcut as /api/control/objects, one endpoint per service: a blue
        // force is not a situation object, and putting both through one endpoint
        // would invent a shape the contract doesn't have.
        await using var factory = new SimulatorFactory();
        var http = factory.CreateClient();

        var blueForce = await http.PostAsync(new Uri("/api/control/blueforces", UriKind.Relative), Json("""
            {
              "blueForcesToUpdates": [
                {
                  "identity": { "stringIdentity": "hand:bf" },
                  "lastContactTime": "2026-09-24T12:00:00Z",
                  "callsign": "HAND"
                }
              ]
            }
            """));

        var position = await http.PostAsync(new Uri("/api/control/position", UriKind.Relative), Json("""
            {
              "position": {
                "sourceIdentifier": "HAND",
                "pointLocation": {
                  "geoPoint": { "latitudeCoordinate": 48.1, "longitudeCoordinate": 11.5 }
                }
              }
            }
            """));

        // Assert
        blueForce.EnsureSuccessStatusCode();
        position.EnsureSuccessStatusCode();

        var stored = await factory.CreateBlueForceClient().GetBlueForcesAsync(new GetBlueForcesRequest());
        Assert.Equal("HAND", Assert.Single(stored.BlueForces).Callsign);

        var fix = await factory.CreateOwnPoseClient().GetPositionAsync(new GetPositionRequest());
        Assert.Equal("HAND", fix.Position.SourceIdentifier);
    }

    [Fact]
    public async Task Pause_FreezesWritesArrivingOverGrpcToo()
    {
        // Arrange - the pause is enforced in the store, so it must apply to the
        // contract's own RPCs and not just to the control surface that set it.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        // Act
        await http.PostAsync(new Uri("/api/control/pause", UriKind.Relative), null);
        var write = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("paused"), T0, "ALPHA") }
        });

        // Assert - refused with an error header, not a transport failure.
        Assert.False(write.Header.Success);
        Assert.Contains("paused", write.Header.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // And accepted again once resumed.
        await http.PostAsync(new Uri("/api/control/resume", UriKind.Relative), null);
        var afterResume = await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol(Unique("resumed"), T0, "ALPHA") }
        });
        Assert.True(afterResume.Header.Success, afterResume.Header.ErrorMessage);
    }

    [Fact]
    public async Task InjectedObject_IsVisibleOverTheContract()
    {
        // Arrange - injection is a shortcut past the transport, not past the
        // semantics: what goes in by hand must come out of GetSituationObjects.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();
        var id = Unique("injected");

        // Act
        var response = await http.PostAsync(
            new Uri("/api/control/objects", UriKind.Relative), Json(SymbolJson(id, T0, "HAND INJECTED")));

        // Assert
        response.EnsureSuccessStatusCode();
        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        var obj = Assert.Single(get.SituationObjects, o => o.Symbol?.Identity?.StringIdentity == id);
        Assert.Equal("HAND INJECTED", obj.Symbol.Name.Content);
    }

    [Fact]
    public async Task InjectedObject_CanBeDeletedAgain()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();
        var id = Unique("injected-delete");

        await http.PostAsync(
            new Uri("/api/control/objects", UriKind.Relative), Json(SymbolJson(id, T0, name: null)));

        // Act
        var response = await http.PostAsync(
            new Uri("/api/control/objects/delete", UriKind.Relative), Json(DeleteJson(id, T0.AddMinutes(1))));

        // Assert
        response.EnsureSuccessStatusCode();
        var get = await client.GetSituationObjectsAsync(new GetSituationObjectsRequest());
        Assert.DoesNotContain(get.SituationObjects, o => o.Symbol?.Identity?.StringIdentity == id);
    }

    [Fact]
    public async Task MalformedBody_IsRejectedWithBadRequest()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var http = factory.CreateClient();

        // Act
        var response = await http.PostAsync(new Uri("/api/control/objects", UriKind.Relative), Json("{not json"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task State_ReportsPauseAndFaultConfiguration()
    {
        // Arrange
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:Faults:Enabled"] = "true",
            ["Simulator:Faults:ErrorHeaderProbability"] = "0.25"
        });
        var http = factory.CreateClient();

        // Act
        var state = await http.GetFromJsonAsync<JsonElement>("/api/control/state");

        // Assert
        Assert.False(state.GetProperty("paused").GetBoolean());
        Assert.True(state.GetProperty("faults").GetProperty("enabled").GetBoolean());
        Assert.Equal(0.25, state.GetProperty("faults").GetProperty("errorHeaderProbability").GetDouble());
    }

    [Fact]
    public async Task Endpoints_AreNotServedWhenDisabled()
    {
        // Arrange - the escape hatch for a run that must only be driveable through
        // the TacticalAPI contract itself.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:Control:Enabled"] = "false"
        });
        var http = factory.CreateClient();

        // Act
        var state = await http.GetAsync(new Uri("/api/control/state", UriKind.Relative));
        var reset = await http.PostAsync(new Uri("/api/control/reset", UriKind.Relative), null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, state.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    /// <summary>
    ///     Canonical protobuf JSON for one Symbol update - the same body shape
    ///     `grpcurl -d` would take, built by hand here so the test exercises the
    ///     endpoint's own parsing rather than a serializer that agrees with it.
    /// </summary>
    private static string SymbolJson(string id, DateTimeOffset reportingTime, string? name)
    {
        var nameProperty = name is null ? string.Empty : $",\"name\":{{\"content\":\"{name}\"}}";
        return "{\"situationObjects\":[{\"symbol\":{"
               + $"\"identity\":{{\"stringIdentity\":\"{id}\"}},"
               + "\"reporter\":{\"stringIdentity\":\"operator\"},"
               + $"\"reportingTime\":\"{Rfc3339(reportingTime)}\""
               + nameProperty
               + "}}]}";
    }

    private static string DeleteJson(string id, DateTimeOffset reportingTime)
    {
        return "{\"situationObjects\":[{"
               + $"\"identity\":{{\"stringIdentity\":\"{id}\"}},"
               + "\"reporter\":{\"stringIdentity\":\"operator\"},"
               + $"\"reportingTime\":\"{Rfc3339(reportingTime)}\""
               + "}]}";
    }

    private static string Rfc3339(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static StringContent Json(string body)
    {
        return new StringContent(body, Encoding.UTF8, "application/json");
    }

    private static async Task<int> ReadIntAsync(HttpResponseMessage response, string property)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty(property).GetInt32();
    }

    private static string Unique(string prefix)
    {
        return $"e2e:{prefix}:{Guid.NewGuid():N}";
    }
}
