using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Sources;

/// <summary>Description of a moving track, expressed in plain values.</summary>
public readonly record struct TrackReport(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    double? AltitudeMeters,
    double? CourseDegrees,
    double? SpeedMetersPerSecond,
    string? AdditionalInformation);

/// <summary>
///     Builds <see cref="UpdateSituationObject" /> messages (type Symbol) straight
///     from track reports - the one mapping shared by all track-like sources so
///     every source speaks the unmodified TacticalAPI update model.
/// </summary>
public static class TrackUpdateFactory
{
    public static UpdateSituationObject CreateSymbolUpdate(
        in TrackReport track,
        string reporterId,
        string symbolCode,
        SymbolCatalog symbolCatalog,
        DateTimeOffset now,
        TimeSpan timeToLive)
    {
        var nowTs = Timestamp.FromDateTimeOffset(now);

        var symbol = new UpdateSymbol
        {
            Identity = new Identity { StringIdentity = track.Id },
            Reporter = new Identity { StringIdentity = reporterId },
            ReportingTime = nowTs,
            ExpiryTime = new UpdatePropertyTimestamp
            {
                Content = Timestamp.FromDateTimeOffset(now + timeToLive)
            },
            Name = new UpdatePropertyString { Content = track.Name },
            Location = new UpdatePropertyLocation
            {
                Content = new SymbolLocation
                {
                    Point = new Point
                    {
                        LocationTime = nowTs,
                        GeoPoint = new GeoPoint
                        {
                            LatitudeCoordinate = track.Latitude,
                            LongitudeCoordinate = track.Longitude,
                            VerticalDistance = track.AltitudeMeters,
                            VerticalDistanceReferenceCode =
                                track.AltitudeMeters is null
                                    ? VerticalDistanceReferenceCode.Unspecified
                                    : VerticalDistanceReferenceCode.MeanSeaLevel,
                            MeasurementCode = MeasurementCode.Gps
                        },
                        Course = track.CourseDegrees,
                        Speed = track.SpeedMetersPerSecond
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(symbolCode))
            symbol.SymbolIdentifier = new UpdatePropertySymbolIdentifier
            {
                Content = new SymbolIdentifier
                {
                    SymbolCatalog = symbolCatalog,
                    StringIdentifier = symbolCode
                }
            };

        if (track.AdditionalInformation is not null)
            symbol.AdditionalInformation = new UpdatePropertyString { Content = track.AdditionalInformation };

        return new UpdateSituationObject { Symbol = symbol };
    }
}
