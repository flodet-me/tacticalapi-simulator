using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;

namespace TacticalApi.Simulator.Sources.OpenSky;

/// <summary>
/// Live data source: polls the public OpenSky Network REST API
/// (https://openskynetwork.github.io/opensky-api/rest.html) for aircraft state
/// vectors inside a configurable bounding box and maps every aircraft onto a
/// TacticalAPI Symbol update with a Point location (position, altitude, course,
/// speed). This is the reference for plugging any online tracker (AIS ship
/// feeds, ADS-B, ...) into the simulator: fetch -> map to TrackReport ->
/// TrackUpdateFactory -> done.
/// </summary>
public sealed class OpenSkySource : ISimulationSource
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenSkyOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenSkySource> _logger;

    public const string HttpClientName = "OpenSky";

    public OpenSkySource(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenSkyOptions> options,
        TimeProvider timeProvider,
        ILogger<OpenSkySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Name => "OpenSky";

    public bool Enabled => _options.CurrentValue.Enabled;

    public TimeSpan Interval => _options.CurrentValue.PollInterval;

    public async Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = options.BaseAddress;

        var url = string.Create(CultureInfo.InvariantCulture,
            $"states/all?lamin={options.MinLatitude}&lomin={options.MinLongitude}&lamax={options.MaxLatitude}&lomax={options.MaxLongitude}");

        using var document = await client.GetFromJsonAsync<JsonDocument>(url, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("states", out var states) ||
            states.ValueKind != JsonValueKind.Array)
        {
            _logger.LogDebug("OpenSky returned no states");
            return [];
        }

        var now = _timeProvider.GetUtcNow();
        var updates = new List<UpdateSituationObject>();

        foreach (var state in states.EnumerateArray())
        {
            if (options.MaxTracksPerPoll > 0 && updates.Count >= options.MaxTracksPerPoll)
            {
                break;
            }

            // State vector layout (indices per OpenSky REST docs):
            // 0 icao24, 1 callsign, 2 origin_country, 5 longitude, 6 latitude,
            // 7 baro_altitude, 8 on_ground, 9 velocity, 10 true_track,
            // 13 geo_altitude.
            var icao24 = GetString(state, 0);
            var longitude = GetDouble(state, 5);
            var latitude = GetDouble(state, 6);
            if (icao24 is null || latitude is null || longitude is null)
            {
                continue;
            }

            var callsign = GetString(state, 1)?.Trim();
            var country = GetString(state, 2);
            var altitude = GetDouble(state, 13) ?? GetDouble(state, 7);
            var onGround = state.GetArrayLength() > 8 && state[8].ValueKind == JsonValueKind.True;

            var track = new TrackReport(
                Id: $"opensky:{icao24}",
                Name: string.IsNullOrEmpty(callsign) ? icao24.ToUpperInvariant() : callsign,
                Latitude: latitude.Value,
                Longitude: longitude.Value,
                AltitudeMeters: onGround ? 0 : altitude,
                CourseDegrees: GetDouble(state, 10),
                SpeedMetersPerSecond: GetDouble(state, 9),
                AdditionalInformation: $"OpenSky live track, origin: {country ?? "unknown"}");

            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, options.ReporterId, options.SymbolCode, options.SymbolCatalog, now, options.TrackTimeToLive));
        }

        _logger.LogDebug("OpenSky produced {Count} track updates", updates.Count);
        return updates;
    }

    private static string? GetString(JsonElement state, int index)
        => state.GetArrayLength() > index && state[index].ValueKind == JsonValueKind.String
            ? state[index].GetString()
            : null;

    private static double? GetDouble(JsonElement state, int index)
        => state.GetArrayLength() > index && state[index].ValueKind == JsonValueKind.Number
            ? state[index].GetDouble()
            : null;
}
