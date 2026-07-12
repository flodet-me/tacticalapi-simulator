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
    internal const string TestReporterId = "TEST";

    internal static IOptionsMonitor<SimulatorOptions> Options(SimulatorOptions? options = null)
    {
        return Options<SimulatorOptions>(options ?? new SimulatorOptions());
    }

    /// <summary>
    ///     A fixed <see cref="IOptionsMonitor{T}" /> for any options type - no reload support, just a constant
    ///     CurrentValue.
    /// </summary>
    internal static IOptionsMonitor<T> Options<T>(T value)
    {
        return new StaticOptionsMonitor<T>(value);
    }

    /// <summary>Reporter/reporting-time pair shared by every merger test's Arrange step.</summary>
    internal static (Identity Reporter, Timestamp Time) Meta(DateTimeOffset reportingTime)
    {
        return (new Identity { StringIdentity = TestReporterId }, Timestamp.FromDateTimeOffset(reportingTime));
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
            Reporter = new Identity { StringIdentity = TestReporterId },
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
            Reporter = new Identity { StringIdentity = TestReporterId },
            ReportingTime = Timestamp.FromDateTimeOffset(reportingTime)
        };
    }

    /// <summary>
    ///     An <see cref="UpdateSituationObject" /> of the given oneof case with only
    ///     Identity/Reporter/ReportingTime/ExpiryTime set - the minimum every
    ///     <c>ISituationObjectMerger</c> needs, for exercising expiry across every
    ///     object type (see SituationStore.GetExpiry/GetIdentity).
    /// </summary>
    internal static UpdateSituationObject ExpirableUpdate(
        SituationObject.TypeOneofCase typeCase, string id, DateTimeOffset reportingTime, DateTimeOffset expiry)
    {
        var identity = new Identity { StringIdentity = id };
        var reporter = new Identity { StringIdentity = TestReporterId };
        var time = Timestamp.FromDateTimeOffset(reportingTime);
        var expiryProperty = new UpdatePropertyTimestamp { Content = Timestamp.FromDateTimeOffset(expiry) };

        return typeCase switch
        {
            SituationObject.TypeOneofCase.Symbol => new UpdateSituationObject
            {
                Symbol = new UpdateSymbol
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.TextDocument => new UpdateSituationObject
            {
                TextDocument = new UpdateTextDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.ActionTask => new UpdateSituationObject
            {
                ActionTask = new UpdateActionTask
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.ActionEvent => new UpdateSituationObject
            {
                ActionEvent = new UpdateActionEvent
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.OrganizationUnit => new UpdateSituationObject
            {
                OrganizationUnit = new UpdateOrganizationUnit
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.Route => new UpdateSituationObject
            {
                Route = new UpdateRoute
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.PictureDocument => new UpdateSituationObject
            {
                PictureDocument = new UpdatePictureDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.VoiceMessageDocument => new UpdateSituationObject
            {
                VoiceMessageDocument = new UpdateVoiceMessageDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.NatoMessageDocument => new UpdateSituationObject
            {
                NatoMessageDocument = new UpdateNatoMessageDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.OverlayDocument => new UpdateSituationObject
            {
                OverlayDocument = new UpdateOverlayDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            SituationObject.TypeOneofCase.SketchDocument => new UpdateSituationObject
            {
                SketchDocument = new UpdateSketchDocument
                { Identity = identity, Reporter = reporter, ReportingTime = time, ExpiryTime = expiryProperty }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(typeCase), typeCase,
                "No expirable update builder for this type.")
        };
    }

    /// <summary>A <see cref="TimeProvider" /> whose "now" is fixed until explicitly advanced.</summary>
    internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }

        public void Advance(TimeSpan by)
        {
            now += by;
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }
}
