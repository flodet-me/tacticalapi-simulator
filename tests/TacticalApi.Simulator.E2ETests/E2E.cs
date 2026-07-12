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
        DateTimeOffset? expiry = null)
    {
        var symbol = new UpdateSymbol
        {
            Identity = new Identity { StringIdentity = id },
            Reporter = new Identity { StringIdentity = "E2E" },
            ReportingTime = Timestamp.FromDateTimeOffset(reportingTime),
        };

        if (name is not null)
        {
            symbol.Name = new UpdatePropertyString { Content = name };
        }

        if (latitude is not null && longitude is not null)
        {
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
                            LongitudeCoordinate = longitude.Value,
                        },
                    },
                },
            };
        }

        if (expiry is not null)
        {
            symbol.ExpiryTime = new UpdatePropertyTimestamp
            {
                Content = Timestamp.FromDateTimeOffset(expiry.Value),
            };
        }

        return new UpdateSituationObject { Symbol = symbol };
    }

    internal static DeleteSituationObject Delete(string id, DateTimeOffset reportingTime) => new()
    {
        Identity = new Identity { StringIdentity = id },
        Reporter = new Identity { StringIdentity = "E2E" },
        ReportingTime = Timestamp.FromDateTimeOffset(reportingTime),
    };
}
