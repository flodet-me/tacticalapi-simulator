using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.E2ETests;

internal static class E2E
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static UpdateSituationObject Symbol(
        string id,
        DateTimeOffset reportingTime,
        string? name = null,
        double? latitude = null,
        double? longitude = null,
        DateTimeOffset? expiry = null,
        SymbolIdentifier? symbolIdentifier = null)
    {
        var symbol = new UpdateSymbol
        {
            Identity = new Identity { StringIdentity = id },
            Reporter = new Identity { StringIdentity = "E2E" },
            ReportingTime = Timestamp.FromDateTimeOffset(reportingTime)
        };

        if (name is not null) symbol.Name = new UpdatePropertyString { Content = name };

        if (latitude is not null && longitude is not null)
            symbol.Location = new UpdatePropertyLocation
            {
                Content = new SymbolLocation
                {
                    Point = new Point
                    {
                        LocationTime = Timestamp.FromDateTimeOffset(reportingTime),
                        GeoPoint = new GeoPoint
                        {
                            LatitudeCoordinate = latitude.Value,
                            LongitudeCoordinate = longitude.Value
                        }
                    }
                }
            };

        if (expiry is not null)
            symbol.ExpiryTime = new UpdatePropertyTimestamp
            {
                Content = Timestamp.FromDateTimeOffset(expiry.Value)
            };

        if (symbolIdentifier is not null)
            symbol.SymbolIdentifier = new UpdatePropertySymbolIdentifier { Content = symbolIdentifier };

        return new UpdateSituationObject { Symbol = symbol };
    }

    internal static UpdateBlueForce BlueForce(
        string id,
        DateTimeOffset lastContactTime,
        string? callsign = null,
        double? latitude = null,
        double? longitude = null,
        Identity? mountHost = null,
        BlueForceType? type = null)
    {
        var update = new UpdateBlueForce
        {
            Identity = new Identity { StringIdentity = id },
            LastContactTime = Timestamp.FromDateTimeOffset(lastContactTime)
        };

        if (callsign is not null) update.Callsign = callsign;
        if (mountHost is not null) update.MountHost = mountHost;
        if (type is not null) update.BlueForceType = type;

        if (latitude is not null && longitude is not null)
            update.PointLocation = new Point
            {
                LocationTime = Timestamp.FromDateTimeOffset(lastContactTime),
                GeoPoint = new GeoPoint
                {
                    LatitudeCoordinate = latitude.Value,
                    LongitudeCoordinate = longitude.Value
                }
            };

        return update;
    }

    internal static UpdatePositionRequest Position(string source, double latitude, double longitude)
    {
        return new UpdatePositionRequest
        {
            Position = new UpdatePosition
            {
                SourceIdentifier = source,
                PointLocation = new Point
                {
                    LocationTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                    GeoPoint = new GeoPoint
                    {
                        LatitudeCoordinate = latitude,
                        LongitudeCoordinate = longitude
                    }
                }
            }
        };
    }

    internal static DeleteSituationObject Delete(string id, DateTimeOffset reportingTime)
    {
        return new DeleteSituationObject
        {
            Identity = new Identity { StringIdentity = id },
            Reporter = new Identity { StringIdentity = "E2E" },
            ReportingTime = Timestamp.FromDateTimeOffset(reportingTime)
        };
    }
}
