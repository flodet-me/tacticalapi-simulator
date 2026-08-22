using System.Collections;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Recording;
using TacticalApi.Simulator.Sources.Synthetic;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SituationObjectToUpdate" />
///     (src/simulator/TacticalApi.Simulator.Core/Recording/SituationObjectToUpdate.cs).
///     The converter is descriptor-driven, so these tests are what stands between it
///     and a silent mis-mapping: the round-trip test below drives every one of the
///     eleven object types through store -> update -> store and demands the two
///     situations come out equal.
/// </summary>
public sealed class SituationObjectToUpdateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Identity Reporter = new() { StringIdentity = "REPLAY" };

    [Fact]
    public async Task Convert_RoundTripsEveryObjectTypeThroughTheStore()
    {
        // Arrange - the synthetic scenario is the one source that emits all eleven
        // types in a single cycle, which makes it the natural fixture here.
        var source = new SyntheticScenarioSource(
            TestHelpers.Options(new SyntheticScenarioOptions { EventProbability = 1, ChatProbability = 1 }),
            TimeProvider.System,
            NullLogger<SyntheticScenarioSource>.Instance);

        var original = TestHelpers.CreateStore();
        original.AddOrUpdate(await source.ProduceAsync(CancellationToken.None));
        var before = original.GetSnapshot();

        // Act - convert every stored object back into the update that would recreate
        // it, then apply those updates to a fresh store.
        var reportingTime = Timestamp.FromDateTimeOffset(T0);
        var updates = before
            .Select(obj => SituationObjectToUpdate.Convert(obj, Reporter, reportingTime))
            .OfType<UpdateSituationObject>()
            .ToList();

        var replayed = TestHelpers.CreateStore();
        var result = replayed.AddOrUpdate(updates);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(before.Count, updates.Count);

        var after = replayed.GetSnapshot();
        Assert.Equal(before.Count, after.Count);

        // Every type must actually have been exercised, or this test would pass
        // while covering nothing.
        var types = before.Select(o => o.TypeCase).ToHashSet();
        foreach (var typeCase in System.Enum.GetValues<SituationObject.TypeOneofCase>()
                     .Where(c => c != SituationObject.TypeOneofCase.None))
            Assert.Contains(typeCase, types);

        foreach (var expected in before)
        {
            var identity = SituationObjectToUpdate.IdentityOf(expected);
            Assert.NotNull(identity);

            var actual = after.SingleOrDefault(o => identity.Equals(SituationObjectToUpdate.IdentityOf(o)));
            Assert.True(actual is not null, $"'{identity.StringIdentity}' did not survive the round trip");
            Assert.Equal(expected.TypeCase, actual.TypeCase);

            // CreationMetaData is stamped by the store from the update's own
            // reporter/reporting time and is deliberately not carried across, so it is
            // normalized away before comparing - everything else must match exactly.
            Assert.Equal(StripMetaData(expected), StripMetaData(actual));
        }
    }

    [Fact]
    public void Convert_CarriesTheIdentityAndTheGivenReporterAndTime()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA", 53.1, 8.8)]);
        var stored = store.GetSnapshot()[0];
        var reportingTime = Timestamp.FromDateTimeOffset(T0.AddMinutes(5));

        // Act
        var update = SituationObjectToUpdate.Convert(stored, Reporter, reportingTime);

        // Assert
        Assert.NotNull(update);
        Assert.Equal("track-1", update.Symbol.Identity.StringIdentity);
        Assert.Equal(Reporter, update.Symbol.Reporter);
        Assert.Equal(reportingTime, update.Symbol.ReportingTime);
        Assert.Equal("ALPHA", update.Symbol.Name.Content);
        Assert.Equal(53.1, update.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void Convert_OmitsPropertiesThatWereNeverSet()
    {
        // Arrange - a property the stored object doesn't carry must stay omitted, not
        // become a present-but-empty update that would clear it on the receiver.
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA")]);

        // Act
        var update = SituationObjectToUpdate.Convert(
            store.GetSnapshot()[0], Reporter, Timestamp.FromDateTimeOffset(T0));

        // Assert
        Assert.NotNull(update);
        Assert.NotNull(update.Symbol.Name);
        Assert.Null(update.Symbol.Location);
        Assert.Null(update.Symbol.StaffComment);
    }

    [Fact]
    public void Convert_ReturnsNullForAnObjectWithNoType()
    {
        Assert.Null(SituationObjectToUpdate.Convert(
            new SituationObject(), Reporter, Timestamp.FromDateTimeOffset(T0)));
    }

    [Fact]
    public void IdentityOf_ReturnsNullForAnObjectWithNoType()
    {
        Assert.Null(SituationObjectToUpdate.IdentityOf(new SituationObject()));
    }

    /// <summary>
    ///     Clears every CreationMetaData in an object so two objects can be compared on
    ///     content alone. Descriptor-driven so it covers whatever the contract holds,
    ///     rather than only the fields this test happened to think of.
    /// </summary>
    private static SituationObject StripMetaData(SituationObject obj)
    {
        var copy = obj.Clone();
        StripMetaData((IMessage)copy);
        return copy;
    }

    private static void StripMetaData(IMessage message)
    {
        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.FieldType != FieldType.Message) continue;

            // Maps (foreign_keys) carry metadata on their values too, and are neither
            // IsRepeated nor a plain message field - missing them here would let a
            // metadata difference masquerade as a content difference.
            if (field.IsMap)
            {
                if (field.Accessor.GetValue(message) is IDictionary map)
                    foreach (var value in map.Values)
                        if (value is IMessage nested)
                            StripMetaData(nested);

                continue;
            }

            if (field.IsRepeated)
            {
                if (field.Accessor.GetValue(message) is IEnumerable items)
                    foreach (var item in items)
                        if (item is IMessage nested)
                            StripMetaData(nested);

                continue;
            }

            if (field.Name == "creation_meta_data")
            {
                field.Accessor.Clear(message);
                continue;
            }

            if (field.Accessor.GetValue(message) is IMessage child) StripMetaData(child);
        }
    }
}
