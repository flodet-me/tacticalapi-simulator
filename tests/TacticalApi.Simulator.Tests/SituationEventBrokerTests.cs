using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Events;
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

        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA")]);

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
