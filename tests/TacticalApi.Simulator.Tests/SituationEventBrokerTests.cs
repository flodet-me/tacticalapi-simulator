using System.Threading.Channels;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SituationEventBroker" />
///     (src/TacticalApi.Simulator.Core/Events/SituationEventBroker.cs).
/// </summary>
public sealed class SituationEventBrokerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Subscriber_ReceivesPublishedChanges()
    {
        // Arrange
        var broker = new SituationEventBroker(TestHelpers.Options());
        var store = TestHelpers.CreateStore(broker: broker);
        using var subscription = broker.Subscribe();

        // Act
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA")]);

        // Assert
        var received = await subscription.Reader.ReadAsync();
        Assert.Equal("ALPHA", received.Symbol.Name.Content);
    }

    [Fact]
    public void Dispose_RemovesSubscriber()
    {
        // Arrange
        var broker = new SituationEventBroker(TestHelpers.Options());
        var subscription = broker.Subscribe();
        Assert.Equal(1, broker.SubscriberCount);

        // Act
        subscription.Dispose();

        // Assert
        Assert.Equal(0, broker.SubscriberCount);
    }

    [Fact]
    public void Publish_WithoutSubscribers_DoesNotThrow()
    {
        // Arrange
        var broker = new SituationEventBroker(TestHelpers.Options());

        // Act
        var exception = Record.Exception(() => broker.Publish([new SituationObject()]));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Publish_ChannelFullInWaitMode_BlocksUntilDrained()
    {
        // Arrange: capacity 1 + Wait mode means a second publish while the
        // channel is still full must apply backpressure via the synchronous
        // WriteAsync fallback (SituationEventBroker.Publish), not drop anything.
        var options = new SimulatorOptions();
        options.Performance.SubscriberChannelCapacity = 1;
        options.Performance.SubscriberChannelFullMode = BoundedChannelFullMode.Wait;
        var broker = new SituationEventBroker(TestHelpers.Options(options));
        using var subscription = broker.Subscribe();

        broker.Publish([Symbol("first")]);

        // Act: this call blocks (on a pool thread) until the reader drains "first".
        // The delay before reading ensures the second publish's TryWrite actually
        // observes the still-full channel and falls back to the blocking write,
        // rather than racing a read that frees capacity first.
        var publishTask = Task.Run(() => broker.Publish([Symbol("second")]));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        var first = await subscription.Reader.ReadAsync();
        await publishTask.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await subscription.Reader.ReadAsync();

        // Assert
        Assert.Equal("first", first.Symbol.Name.Content);
        Assert.Equal("second", second.Symbol.Name.Content);
    }

    private static SituationObject Symbol(string name)
    {
        return new SituationObject { Symbol = new Symbol { Name = new DataPropertyString { Content = name } } };
    }
}
