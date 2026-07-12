using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TacticalApi.Simulator.Sources.OpenSky;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="OpenSkySource" />
///     (src/TacticalApi.Simulator.Sources.OpenSky/OpenSkySource.cs), against a
///     stubbed <see cref="IHttpClientFactory" /> - no real network calls.
/// </summary>
public sealed class OpenSkySourceTests
{
    [Fact]
    public void Source_ExposesNameAndIntervalFromOptions()
    {
        // Arrange
        var options = new OpenSkyOptions { Enabled = true, PollInterval = TimeSpan.FromSeconds(42) };
        var source = CreateSource(StatesResponse(), options);

        // Act & Assert
        Assert.Equal("OpenSky", source.Name);
        Assert.True(source.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(42), source.Interval);
    }

    [Fact]
    public async Task ProduceAsync_MapsStateVectorFieldsToSymbolUpdate()
    {
        // Arrange
        var options = new OpenSkyOptions { ReporterId = "SIM-OPENSKY" };
        var source = CreateSource(StatesResponse(StateRow()), options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var symbol = Assert.Single(updates).Symbol;
        Assert.Equal("opensky:4840d6", symbol.Identity.StringIdentity);
        Assert.Equal("SIM-OPENSKY", symbol.Reporter.StringIdentity);
        Assert.Equal("BAW123", symbol.Name.Content);
        var point = symbol.Location.Content.Point;
        Assert.Equal(51.5, point.GeoPoint.LatitudeCoordinate);
        Assert.Equal(-0.5, point.GeoPoint.LongitudeCoordinate);
        Assert.Equal(3100, point.GeoPoint.VerticalDistance); // geo_altitude preferred over baro_altitude
        Assert.Equal(90, point.Course);
        Assert.Equal(200, point.Speed);
    }

    [Fact]
    public async Task ProduceAsync_BlankCallsign_FallsBackToUppercasedIcao24()
    {
        // Arrange
        var source = CreateSource(StatesResponse(StateRow("a1b2c3", "   ")));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal("A1B2C3", Assert.Single(updates).Symbol.Name.Content);
    }

    [Fact]
    public async Task ProduceAsync_AircraftOnGround_ForcesAltitudeToZero()
    {
        // Arrange
        var source = CreateSource(StatesResponse(StateRow(onGround: true, geoAltitude: 500, baroAltitude: 400)));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, Assert.Single(updates).Symbol.Location.Content.Point.GeoPoint.VerticalDistance);
    }

    [Fact]
    public async Task ProduceAsync_MissingGeoAltitude_FallsBackToBaroAltitude()
    {
        // Arrange
        var source = CreateSource(StatesResponse(StateRow(geoAltitude: null, baroAltitude: 1234)));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1234, Assert.Single(updates).Symbol.Location.Content.Point.GeoPoint.VerticalDistance);
    }

    [Fact]
    public async Task ProduceAsync_SkipsRowsMissingIcao24OrPosition()
    {
        // Arrange: no identity, no position, and one valid row.
        var source = CreateSource(StatesResponse(
            StateRow(null),
            StateRow(latitude: null),
            StateRow("valid1")));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal("opensky:valid1", Assert.Single(updates).Symbol.Identity.StringIdentity);
    }

    [Fact]
    public async Task ProduceAsync_CapsResultsAtMaxTracksPerPoll()
    {
        // Arrange
        var options = new OpenSkyOptions { MaxTracksPerPoll = 2 };
        var source = CreateSource(
            StatesResponse(StateRow("a1"), StateRow("a2"), StateRow("a3")), options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public async Task ProduceAsync_ResponseWithoutStates_ReturnsEmpty()
    {
        // Arrange: OpenSky returns a null "states" array over regions with no traffic.
        var source = CreateSource("""{"time":1,"states":null}""");

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Empty(updates);
    }

    [Fact]
    public async Task ProduceAsync_RequestsConfiguredBoundingBox()
    {
        // Arrange
        Uri? requestedUri = null;
        var options = new OpenSkyOptions { MinLatitude = 1, MaxLatitude = 2, MinLongitude = 3, MaxLongitude = 4 };
        var source = CreateSource(StatesResponse(), options, uri => requestedUri = uri);

        // Act
        await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(requestedUri);
        Assert.Contains("lamin=1", requestedUri.Query);
        Assert.Contains("lomin=3", requestedUri.Query);
        Assert.Contains("lamax=2", requestedUri.Query);
        Assert.Contains("lomax=4", requestedUri.Query);
    }

    private static OpenSkySource CreateSource(string responseJson, OpenSkyOptions? options = null,
        Action<Uri>? onRequest = null)
    {
        var factory = new StubHttpClientFactory(new StubHandler(responseJson, onRequest));
        return new OpenSkySource(factory, TestHelpers.Options(options ?? new OpenSkyOptions()), TimeProvider.System,
            NullLogger<OpenSkySource>.Instance);
    }

    /// <summary>One OpenSky state-vector row (see the field index map in OpenSkySource.ProduceAsync).</summary>
    private static string StateRow(
        string? icao24 = "4840d6",
        string? callsign = "BAW123",
        string? country = "United Kingdom",
        double? longitude = -0.5,
        double? latitude = 51.5,
        double? baroAltitude = 3000,
        bool onGround = false,
        double? velocity = 200,
        double? trueTrack = 90,
        double? geoAltitude = 3100)
    {
        return $"[{J(icao24)},{J(callsign)},{J(country)},null,null,{J(longitude)},{J(latitude)},{J(baroAltitude)}," +
               $"{J(onGround)},{J(velocity)},{J(trueTrack)},null,null,{J(geoAltitude)}]";
    }

    private static string StatesResponse(params string[] rows)
    {
        return $$"""{"time":1,"states":[{{string.Join(",", rows)}}]}""";
    }

    private static string J(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private sealed class StubHandler(string responseBody, Action<Uri>? onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler);
        }
    }
}
