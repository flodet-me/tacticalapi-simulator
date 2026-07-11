using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Merging;
using TacticalApi.Simulator.Core.Store;

namespace TacticalApi.Simulator.Tests;

internal static class TestHelpers
{
    internal static IOptionsMonitor<SimulatorOptions> Options(SimulatorOptions? options = null)
    {
        return new StaticOptionsMonitor(options ?? new SimulatorOptions());
    }

    internal static SituationStore CreateStore(SimulatorOptions? options = null, SituationEventBroker? broker = null)
    {
        var monitor = Options(options);
        return new SituationStore(
            AllMergers.CreateAll(),
            broker ?? new SituationEventBroker(monitor),
            monitor,
            NullLogger<SituationStore>.Instance);
    }

    internal static UpdateSituationObject SymbolUpdate(
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
            Reporter = new Identity { StringIdentity = "TEST" },
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

        return new UpdateSituationObject { Symbol = symbol };
    }

    internal static DeleteSituationObject Delete(string id, DateTimeOffset reportingTime)
    {
        return new DeleteSituationObject
        {
            Identity = new Identity { StringIdentity = id },
            Reporter = new Identity { StringIdentity = "TEST" },
            ReportingTime = Timestamp.FromDateTimeOffset(reportingTime)
        };
    }

    private sealed class StaticOptionsMonitor(SimulatorOptions value) : IOptionsMonitor<SimulatorOptions>
    {
        public SimulatorOptions CurrentValue => value;

        public SimulatorOptions Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<SimulatorOptions, string?> listener)
        {
            return null;
        }
    }
}
