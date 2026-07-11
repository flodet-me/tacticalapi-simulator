using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Sources;
using Xunit;

namespace TacticalApi.Simulator.Tests;

public sealed class SituationEventBrokerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Subscriber_ReceivesPublishedChanges()
    {
        var broker = new SituationEventBroker(TestHelpers.Options());
        var store = TestHelpers.CreateStore(broker: broker);
        using var subscription = broker.Subscribe();

        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, name: "ALPHA")]);

        var received = await subscription.Reader.ReadAsync();
        Assert.Equal("ALPHA", received.Symbol.Name.Content);
    }

    [Fact]
    public void Dispose_RemovesSubscriber()
    {
        var broker = new SituationEventBroker(TestHelpers.Options());

        var subscription = broker.Subscribe();
        Assert.Equal(1, broker.SubscriberCount);

        subscription.Dispose();
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public void Publish_WithoutSubscribers_DoesNotThrow()
    {
        var broker = new SituationEventBroker(TestHelpers.Options());
        broker.Publish([new SituationObject()]);
    }
}

public sealed class TrackUpdateFactoryTests
{
    [Fact]
    public void CreateSymbolUpdate_MapsAllTrackFields()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var track = new TrackReport(
            Id: "opensky:abc123",
            Name: "DLH123",
            Latitude: 53.05,
            Longitude: 8.79,
            AltitudeMeters: 10_000,
            CourseDegrees: 270,
            SpeedMetersPerSecond: 230,
            AdditionalInformation: "test");

        var update = TrackUpdateFactory.CreateSymbolUpdate(
            track, "REPORTER", "SNAPCF---------", SymbolCatalog.Mil2525C, now, TimeSpan.FromMinutes(2));

        Assert.Equal(UpdateSituationObject.TypeOneofCase.Symbol, update.TypeCase);
        var symbol = update.Symbol;
        Assert.Equal("opensky:abc123", symbol.Identity.StringIdentity);
        Assert.Equal("REPORTER", symbol.Reporter.StringIdentity);
        Assert.Equal("DLH123", symbol.Name.Content);
        Assert.Equal("SNAPCF---------", symbol.SymbolIdentifier.Content.StringIdentifier);
        Assert.Equal(SymbolCatalog.Mil2525C, symbol.SymbolIdentifier.Content.SymbolCatalog);

        var point = symbol.Location.Content.Point;
        Assert.Equal(53.05, point.GeoPoint.LatitudeCoordinate);
        Assert.Equal(8.79, point.GeoPoint.LongitudeCoordinate);
        Assert.Equal(10_000, point.GeoPoint.VerticalDistance);
        Assert.Equal(270, point.Course);
        Assert.Equal(230, point.Speed);

        Assert.Equal(now.AddMinutes(2), symbol.ExpiryTime.Content.ToDateTimeOffset());
    }

    [Fact]
    public void CreateSymbolUpdate_WithoutSymbolCode_LeavesIdentifierUnset()
    {
        var track = new TrackReport("id", "NAME", 0, 0, null, null, null, null);

        var update = TrackUpdateFactory.CreateSymbolUpdate(
            track, "R", string.Empty, SymbolCatalog.Unspecified, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        Assert.Null(update.Symbol.SymbolIdentifier);
        Assert.Null(update.Symbol.Location.Content.Point.GeoPoint.VerticalDistance);
    }
}
