using Rheinmetall.TacticalApi.V0;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     End-to-end tests for the <c>/metrics</c> scrape endpoint
///     (src/simulator/TacticalApi.Simulator.Host/Diagnostics/).
/// </summary>
public sealed class MetricsEndpointE2ETests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Metrics_ExposesEveryInstrumentBeforeAnythingHasHappened()
    {
        // Arrange - a metric that only appears once something has gone wrong is the
        // one nobody has an alert on, because it wasn't there when the alert was
        // written. Every counter must be scrapeable at zero from the start.
        await using var factory = new SimulatorFactory();
        var http = factory.CreateClient();

        // Act
        var body = await http.GetStringAsync(new Uri("/metrics", UriKind.Relative));

        // Assert
        Assert.Contains("tacticalapi_updates_applied_total 0", body, StringComparison.Ordinal);
        Assert.Contains("tacticalapi_subscriber_events_dropped_total 0", body, StringComparison.Ordinal);
        Assert.Contains("tacticalapi_situation_objects 0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_AreValidPrometheusExpositionFormat()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var http = factory.CreateClient();

        // Act
        var response = await http.GetAsync(new Uri("/metrics", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        // HELP/TYPE belong to the metric, not the series: a repeated header for the
        // same name makes a scraper reject the whole payload.
        var typeLines = body.Split('\n').Where(l => l.StartsWith("# TYPE ", StringComparison.Ordinal)).ToList();
        Assert.Equal(typeLines.Count, typeLines.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(typeLines, l => l.EndsWith(" counter", StringComparison.Ordinal));
        Assert.Contains(typeLines, l => l.EndsWith(" gauge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Metrics_CountWritesMadeOverTheContract()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        // Act
        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol("e2e:metrics:1", T0, "ALPHA"), E2E.Symbol("e2e:metrics:2", T0, "BRAVO") }
        });
        var body = await http.GetStringAsync(new Uri("/metrics", UriKind.Relative));

        // Assert
        Assert.Contains("tacticalapi_updates_applied_total 2", body, StringComparison.Ordinal);
        Assert.Contains("tacticalapi_situation_objects 2", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_CountInjectedFaultsByKind()
    {
        // Arrange - so a confusing client-side failure can be traced back to the
        // fault that caused it instead of being mistaken for a real bug.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:Faults:Enabled"] = "true",
            ["Simulator:Faults:ErrorHeaderProbability"] = "1.0"
        });
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        // Act
        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol("e2e:metrics:fault", T0, "ALPHA") }
        });
        var body = await http.GetStringAsync(new Uri("/metrics", UriKind.Relative));

        // Assert
        Assert.Contains("tacticalapi_faults_injected_total{kind=\"error-header\"} 1", body, StringComparison.Ordinal);
    }
}
