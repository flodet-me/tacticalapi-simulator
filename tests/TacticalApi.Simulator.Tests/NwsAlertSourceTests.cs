using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Sources.Nws;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="NwsAlertSource" />
///     (src/TacticalApi.Simulator.Sources.Nws/NwsAlertSource.cs), against a
///     stubbed <see cref="IHttpClientFactory" /> - no real network calls.
/// </summary>
public sealed class NwsAlertSourceTests
{
    [Fact]
    public void Source_ExposesNameAndIntervalFromOptions()
    {
        // Arrange
        var options = new NwsOptions { Enabled = true, PollInterval = TimeSpan.FromMinutes(3) };
        var source = CreateSource(FeaturesResponse(), options);

        // Act & Assert
        Assert.Equal("Nws", source.Name);
        Assert.True(source.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(3), source.Interval);
    }

    [Fact]
    public async Task ProduceAsync_AlertWithoutGeometry_EmitsOnlyTextDocument()
    {
        // Arrange: most alerts (e.g. area-based statements) carry no polygon.
        var source = CreateSource(FeaturesResponse(Feature(id: "a1", eventName: "Beach Hazards Statement")));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        var textDocument = Assert.Single(updates).TextDocument;
        Assert.Equal("Beach Hazards Statement", textDocument.Name.Content);
        Assert.Equal(MessageCategoryType.Warning, textDocument.MessageCategory.Content);
    }

    [Fact]
    public async Task ProduceAsync_AlertWithPolygon_EmitsTextDocumentSymbolAndSketch()
    {
        // Arrange
        var source = CreateSource(FeaturesResponse(Feature(
            id: "a2", eventName: "Flood Warning",
            polygon: "[-96,36],[-95,36],[-95,37],[-96,37],[-96,36]")));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert: one alert with a polygon produces all three DIFFERENT object types.
        Assert.Equal(3, updates.Count);
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.TextDocument);
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol);
        Assert.Contains(updates, u => u.TypeCase == UpdateSituationObject.TypeOneofCase.SketchDocument);

        var symbol = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.Symbol).Symbol;
        var point = symbol.Location.Content.Point.GeoPoint;
        // Plain average of all 5 ring points (GeoJSON repeats the first point to
        // close the ring, so it's weighted double) - not a true polygon centroid.
        Assert.Equal(36.4, point.LatitudeCoordinate, precision: 3);
        Assert.Equal(-95.6, point.LongitudeCoordinate, precision: 3);

        var sketch = updates.Single(u => u.TypeCase == UpdateSituationObject.TypeOneofCase.SketchDocument)
            .SketchDocument;
        Assert.Equal(5, sketch.Location.Content.Line.Points.Count);
    }

    [Theory]
    [InlineData("Extreme", MessagePrecedenceType.Flash)]
    [InlineData("Severe", MessagePrecedenceType.Immediate)]
    [InlineData("Moderate", MessagePrecedenceType.Priority)]
    [InlineData("Minor", MessagePrecedenceType.Routine)]
    [InlineData("Unknown", MessagePrecedenceType.Routine)]
    public async Task ProduceAsync_MapsCapSeverityToMessagePrecedence(string severity, MessagePrecedenceType expected)
    {
        // Arrange
        var source = CreateSource(FeaturesResponse(Feature(id: "a3", eventName: "Test Alert", severity: severity)));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, Assert.Single(updates).TextDocument.MessagePrecedence.Content);
    }

    [Fact]
    public async Task ProduceAsync_SkipsFeaturesMissingIdOrEvent()
    {
        // Arrange
        var source = CreateSource(FeaturesResponse(
            Feature(id: null, eventName: "Has no id"),
            Feature(id: "a4", eventName: null),
            Feature(id: "a5", eventName: "Valid Alert")));

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Valid Alert", Assert.Single(updates).TextDocument.Name.Content);
    }

    [Fact]
    public async Task ProduceAsync_CapsResultsAtMaxAlertsPerPoll()
    {
        // Arrange
        var options = new NwsOptions { MaxAlertsPerPoll = 1 };
        var source = CreateSource(
            FeaturesResponse(Feature(id: "a6", eventName: "One"), Feature(id: "a7", eventName: "Two")), options);

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Single(updates);
    }

    [Fact]
    public async Task ProduceAsync_ResponseWithoutFeatures_ReturnsEmpty()
    {
        // Arrange
        var source = CreateSource("""{"type":"FeatureCollection"}""");

        // Act
        var updates = await source.ProduceAsync(CancellationToken.None);

        // Assert
        Assert.Empty(updates);
    }

    private static NwsAlertSource CreateSource(string responseJson, NwsOptions? options = null)
    {
        var factory = new StubHttpClientFactory(new StubHandler(responseJson));
        return new NwsAlertSource(factory, TestHelpers.Options(options ?? new NwsOptions()), TimeProvider.System,
            NullLogger<NwsAlertSource>.Instance);
    }

    private static string Feature(
        string? id, string? eventName, string? severity = "Severe", string? polygon = null)
    {
        var geometry = polygon is null ? "null" : $$"""{"type":"Polygon","coordinates":[[{{polygon}}]]}""";
        var idJson = id is null ? "null" : $"\"{id}\"";
        var eventJson = eventName is null ? "null" : $"\"{eventName}\"";

        return $$"""
                 {
                   "id": {{idJson}},
                   "geometry": {{geometry}},
                   "properties": {
                     "id": {{idJson}},
                     "event": {{eventJson}},
                     "severity": "{{severity}}",
                     "headline": "headline",
                     "description": "description",
                     "areaDesc": "area",
                     "sent": "2026-07-12T02:14:00-07:00",
                     "expires": "2026-07-12T15:00:00-07:00"
                   }
                 }
                 """;
    }

    private static string FeaturesResponse(params string[] features)
    {
        return $$"""{"type":"FeatureCollection","features":[{{string.Join(",", features)}}]}""";
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
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
