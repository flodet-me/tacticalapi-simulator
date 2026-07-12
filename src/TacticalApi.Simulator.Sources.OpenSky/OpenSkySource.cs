using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.OpenSky;

/// <summary>
///     Live data source: polls the public OpenSky Network REST API
///     (https://openskynetwork.github.io/opensky-api/rest.html) for aircraft state
///     vectors inside a configurable bounding box and maps every aircraft onto a
///     TacticalAPI Symbol update with a Point location (position, altitude, course,
///     speed). This is the reference for plugging any online tracker (AIS ship
///     feeds, ADS-B, ...) into the simulator: fetch -> map to TrackReport ->
///     TrackUpdateFactory -> done.
/// </summary>
public sealed class OpenSkySource(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenSkyOptions> options,
    TimeProvider timeProvider,
    ILogger<OpenSkySource> logger)
    : ISimulationSource
{
    public static readonly string HttpClientName = SimulationSourceName.FromSectionName(OpenSkyOptions.SectionName);

    public string Name => HttpClientName;

    public bool Enabled => options.CurrentValue.Enabled;

    public TimeSpan Interval => options.CurrentValue.PollInterval;

    public async Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var options1 = options.CurrentValue;
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = options1.BaseAddress;

        var url = string.Create(CultureInfo.InvariantCulture,
            $"states/all?lamin={options1.MinLatitude}&lomin={options1.MinLongitude}&lamax={options1.MaxLatitude}&lomax={options1.MaxLongitude}");

        using var document = await client.GetFromJsonAsync<JsonDocument>(url, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("states", out var states) ||
            states.ValueKind != JsonValueKind.Array)
        {
            logger.LogDebug("OpenSky returned no states");
            return [];
        }

        var now = timeProvider.GetUtcNow();
        var updates = new List<UpdateSituationObject>();

        foreach (var state in states.EnumerateArray())
        {
            if (options1.MaxTracksPerPoll > 0 && updates.Count >= options1.MaxTracksPerPoll) break;

            // State vector layout (indices per OpenSky REST docs):
            // 0 icao24, 1 callsign, 2 origin_country, 5 longitude, 6 latitude,
            // 7 baro_altitude, 8 on_ground, 9 velocity, 10 true_track,
            // 13 geo_altitude.
            var icao24 = GetString(state, 0);
            var longitude = GetDouble(state, 5);
            var latitude = GetDouble(state, 6);
            if (icao24 is null || latitude is null || longitude is null) continue;

            var callsign = GetString(state, 1)?.Trim();
            var country = GetString(state, 2);
            var altitude = GetDouble(state, 13) ?? GetDouble(state, 7);
            var onGround = state.GetArrayLength() > 8 && state[8].ValueKind == JsonValueKind.True;

            var track = new TrackReport(
                $"opensky:{icao24}",
                string.IsNullOrEmpty(callsign) ? icao24.ToUpperInvariant() : callsign,
                latitude.Value,
                longitude.Value,
                onGround ? 0 : altitude,
                GetDouble(state, 10),
                GetDouble(state, 9),
                $"OpenSky live track, origin: {country ?? "unknown"}");

            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, options1.ReporterId, options1.SymbolCode, options1.SymbolCatalog, now,
                options1.TrackTimeToLive));
        }

        logger.LogDebug("OpenSky produced {Count} track updates", updates.Count);
        return updates;
    }

    private static string? GetString(JsonElement state, int index)
    {
        return state.GetArrayLength() > index && state[index].ValueKind == JsonValueKind.String
            ? state[index].GetString()
            : null;
    }

    private static double? GetDouble(JsonElement state, int index)
    {
        return state.GetArrayLength() > index && state[index].ValueKind == JsonValueKind.Number
            ? state[index].GetDouble()
            : null;
    }
}
