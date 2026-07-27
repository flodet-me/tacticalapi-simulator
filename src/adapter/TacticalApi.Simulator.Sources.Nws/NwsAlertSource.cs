using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Sources;
using TacticalApi.Simulator.Sources.Nws.Logging;

namespace TacticalApi.Simulator.Sources.Nws;

/// <summary>
///     Live data source: polls the public US National Weather Service active
///     alerts API (https://www.weather.gov/documentation/services-web-api) for
///     one state and maps every alert onto the TacticalAPI update model. Unlike
///     the track-only sources, a single NWS alert produces up to three DIFFERENT
///     situation object types from one feed: a <see cref="TextDocument" /> for
///     the alert text (always), and - only when NWS attaches a warning polygon
///     - a <see cref="Symbol" /> marker at its centroid plus a
///     <see cref="SketchDocument" /> outlining the affected area.
/// </summary>
public sealed class NwsAlertSource(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<NwsOptions> options,
    TimeProvider timeProvider,
    ILogger<NwsAlertSource> logger)
    : ISimulationSource
{
    /// <summary>Name of the named <see cref="HttpClient" /> registered for this source.</summary>
    public static readonly string HttpClientName = SimulationSourceName.FromSectionName(NwsOptions.SectionName);

    /// <inheritdoc/>
    public string Name => HttpClientName;

    /// <inheritdoc/>
    public bool Enabled => options.CurrentValue.Enabled;

    /// <inheritdoc/>
    public TimeSpan Interval => options.CurrentValue.PollInterval;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UpdateSituationObject>> ProduceAsync(CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = o.BaseAddress;

        // status=actual excludes NWS test/exercise broadcasts, which otherwise
        // show up in the same feed as real alerts (rare, but it happens).
        using var document = await client
            .GetFromJsonAsync<JsonDocument>($"alerts/active?area={o.Area}&status=actual", cancellationToken)
            .ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            logger.NoActiveAlerts(o.Area);
            return [];
        }

        var now = timeProvider.GetUtcNow();
        var reporter = new Identity { StringIdentity = o.ReporterId };
        var updates = new List<UpdateSituationObject>();
        var skipped = 0;
        var noGeometry = 0;
        var capReached = false;

        foreach (var feature in features.EnumerateArray())
        {
            if (o.MaxAlertsPerPoll > 0 && updates.Count >= o.MaxAlertsPerPoll)
            {
                capReached = true;
                break;
            }

            if (!feature.TryGetProperty("properties", out var props)) continue;

            var alertId = GetString(props, "id");
            var eventName = GetString(props, "event");
            if (alertId is null || eventName is null)
            {
                logger.AlertSkippedMissingFields();
                skipped++;
                continue;
            }

            var sent = GetDateTimeOffset(props, "sent") ?? now;
            var expires = GetDateTimeOffset(props, "expires");
            var reportingTime = Timestamp.FromDateTimeOffset(sent);
            var expiryTime = new UpdatePropertyTimestamp
            {
                Content = Timestamp.FromDateTimeOffset(expires ?? now + o.TrackTimeToLive)
            };
            var precedence = SeverityToPrecedence(GetString(props, "severity"));

            updates.Add(TextDocumentUpdate(
                $"nws:text:{alertId}", reporter, reportingTime, expiryTime, eventName,
                GetString(props, "headline"), GetString(props, "description"), precedence));

            var ring = OuterRing(feature);
            if (ring.Count == 0)
            {
                logger.AlertHasNoGeometry(alertId);
                noGeometry++;
                continue;
            }

            var (lat, lon) = Centroid(ring);
            var timeToLive = expires is { } e ? e - now : o.TrackTimeToLive;
            var track = new TrackReport($"nws:symbol:{alertId}", eventName, lat, lon, null, null, null,
                GetString(props, "areaDesc"));
            updates.Add(TrackUpdateFactory.CreateSymbolUpdate(
                track, o.ReporterId, o.SymbolCode, o.SymbolCatalog, now, timeToLive));

            updates.Add(SketchUpdate(
                $"nws:sketch:{alertId}", reporter, reportingTime, expiryTime, eventName, ring, precedence));
        }

        if (capReached) logger.AlertCapReached(o.MaxAlertsPerPoll);
        logger.AlertsProduced(updates.Count, o.Area, skipped, noGeometry);
        return updates;
    }

    private static UpdateSituationObject TextDocumentUpdate(
        string id, Identity reporter, Timestamp reportingTime, UpdatePropertyTimestamp expiryTime, string eventName,
        string? headline, string? description, MessagePrecedenceType precedence)
    {
        return new UpdateSituationObject
        {
            TextDocument = new UpdateTextDocument
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = reportingTime,
                ExpiryTime = expiryTime,
                Name = new UpdatePropertyString { Content = eventName },
                PlainContent = new UpdatePropertyString { Content = headline ?? eventName },
                Content = new UpdatePropertyString { Content = description ?? headline ?? eventName },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Warning },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = precedence }
            }
        };
    }

    private static UpdateSituationObject SketchUpdate(
        string id, Identity reporter, Timestamp reportingTime, UpdatePropertyTimestamp expiryTime, string eventName,
        IReadOnlyList<(double Lat, double Lon)> ring, MessagePrecedenceType precedence)
    {
        var line = new Line { LocationTime = reportingTime, Name = eventName };
        foreach (var (lat, lon) in ring)
            line.Points.Add(new GeoPoint { LatitudeCoordinate = lat, LongitudeCoordinate = lon });

        return new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = id },
                Reporter = reporter,
                ReportingTime = reportingTime,
                ExpiryTime = expiryTime,
                Name = new UpdatePropertyString { Content = eventName },
                Location = new UpdatePropertyLocation { Content = new SymbolLocation { Line = line } },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Warning },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = precedence }
            }
        };
    }

    /// <summary>Maps CAP severity (Extreme/Severe/Moderate/Minor/Unknown) onto message precedence.</summary>
    private static MessagePrecedenceType SeverityToPrecedence(string? severity)
    {
        return severity switch
        {
            "Extreme" => MessagePrecedenceType.Flash,
            "Severe" => MessagePrecedenceType.Immediate,
            "Moderate" => MessagePrecedenceType.Priority,
            _ => MessagePrecedenceType.Routine
        };
    }

    /// <summary>The outer ring of a GeoJSON Polygon geometry, or empty if the feature has none (area-only alert).</summary>
    private static List<(double Lat, double Lon)> OuterRing(JsonElement feature)
    {
        var ring = new List<(double Lat, double Lon)>();
        if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
            return ring;
        if (!geometry.TryGetProperty("type", out var type) || type.GetString() != "Polygon") return ring;
        if (!geometry.TryGetProperty("coordinates", out var coordinates) ||
            coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() == 0)
            return ring;

        // GeoJSON coordinates are [longitude, latitude]; the first ring is the outer boundary.
        foreach (var point in coordinates[0].EnumerateArray())
            if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                ring.Add((point[1].GetDouble(), point[0].GetDouble()));

        return ring;
    }

    private static (double Lat, double Lon) Centroid(IReadOnlyList<(double Lat, double Lon)> ring)
    {
        return (ring.Average(p => p.Lat), ring.Average(p => p.Lon));
    }

    private static string? GetString(JsonElement obj, string property)
    {
        return obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement obj, string property)
    {
        return GetString(obj, property) is { } text &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
    }
}
