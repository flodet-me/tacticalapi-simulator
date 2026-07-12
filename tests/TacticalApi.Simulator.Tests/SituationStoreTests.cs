using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Store;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SituationStore" />
///     (src/TacticalApi.Simulator.Core/Store/SituationStore.cs).
/// </summary>
public sealed class SituationStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddOrUpdate_CreatesNewSymbol()
    {
        // Arrange
        var store = TestHelpers.CreateStore();

        // Act
        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA", 53.0, 8.8)]);

        // Assert
        Assert.True(result.Success);
        var snapshot = store.GetSnapshot();
        var obj = Assert.Single(snapshot);
        Assert.Equal(SituationObject.TypeOneofCase.Symbol, obj.TypeCase);
        Assert.Equal("ALPHA", obj.Symbol.Name.Content);
        Assert.Equal(53.0, obj.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void AddOrUpdate_MergesOnlyProvidedProperties()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "ALPHA", 53.0, 8.8)]);

        // Act: second update moves the track but does not touch the name.
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(5), latitude: 54.0, longitude: 9.0)]);

        // Assert
        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("ALPHA", obj.Symbol.Name.Content);
        Assert.Equal(54.0, obj.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void AddOrUpdate_IgnoresStaleUpdates()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, "NEW")]);

        // Act
        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(-30), "STALE")]);

        // Assert
        Assert.True(result.Success);
        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("NEW", obj.Symbol.Name.Content);
    }

    [Fact]
    public void AddOrUpdate_FailsForMissingIdentity()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        var update = new UpdateSituationObject { Symbol = new UpdateSymbol() };

        // Act
        var result = store.AddOrUpdate([update]);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void AddOrUpdate_FailsForMissingReportingTime()
    {
        // Arrange: identity present, but reporting_time omitted entirely.
        var store = TestHelpers.CreateStore();
        var update = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol { Identity = new Identity { StringIdentity = "track-1" } }
        };

        // Act
        var result = store.AddOrUpdate([update]);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("reporting_time", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddOrUpdate_FailsForUpdateWithoutType()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        var update = new UpdateSituationObject(); // oneof not set

        // Act
        var result = store.AddOrUpdate([update]);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddOrUpdate_EnforcesMaxObjectLimit()
    {
        // Arrange
        var options = new SimulatorOptions();
        options.Performance.MaxSituationObjects = 1;
        var store = TestHelpers.CreateStore(options);

        // Act & Assert
        Assert.True(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]).Success);
        Assert.False(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-2", T0)]).Success);

        // Updating an existing object must still work at the limit.
        Assert.True(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(1), "STILL-OK")]).Success);
    }

    [Fact]
    public void Delete_MarksObjectDeleted_AndSnapshotExcludesIt()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        // Act
        var result = store.Delete([TestHelpers.Delete("track-1", T0.AddSeconds(1))]);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void Delete_UnknownObject_IsNoOp()
    {
        // Arrange
        var store = TestHelpers.CreateStore();

        // Act
        var result = store.Delete([TestHelpers.Delete("ghost", T0)]);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void Delete_AlreadyDeletedObject_IsNoOp()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);
        store.Delete([TestHelpers.Delete("track-1", T0.AddSeconds(1))]);

        // Act: deleting the same object again must not fail or republish it.
        var result = store.Delete([TestHelpers.Delete("track-1", T0.AddSeconds(2))]);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void SweepExpired_DeletesExpiredObjects()
    {
        // Arrange
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([
            TestHelpers.SymbolUpdate("expired", T0, expiry: T0.AddSeconds(10)),
            TestHelpers.SymbolUpdate("alive", T0, expiry: T0.AddHours(1))
        ]);

        // Act
        var swept = store.SweepExpired(T0.AddMinutes(1), "SWEEPER");

        // Assert
        Assert.Equal(1, swept);
        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("alive", obj.Symbol.Identity.StringIdentity);
    }

    [Theory]
    [InlineData(SituationObject.TypeOneofCase.TextDocument)]
    [InlineData(SituationObject.TypeOneofCase.ActionTask)]
    [InlineData(SituationObject.TypeOneofCase.ActionEvent)]
    [InlineData(SituationObject.TypeOneofCase.OrganizationUnit)]
    [InlineData(SituationObject.TypeOneofCase.Route)]
    [InlineData(SituationObject.TypeOneofCase.PictureDocument)]
    [InlineData(SituationObject.TypeOneofCase.VoiceMessageDocument)]
    [InlineData(SituationObject.TypeOneofCase.NatoMessageDocument)]
    [InlineData(SituationObject.TypeOneofCase.OverlayDocument)]
    [InlineData(SituationObject.TypeOneofCase.SketchDocument)]
    public void SweepExpired_DeletesExpiredObjects_ForEveryObjectType(SituationObject.TypeOneofCase typeCase)
    {
        // Arrange: SweepExpired's GetExpiry/GetIdentity switch has one arm per
        // object type - Symbol is covered above, this exercises the rest.
        var store = TestHelpers.CreateStore();
        var update = TestHelpers.ExpirableUpdate(typeCase, "expiring-1", T0, T0.AddSeconds(10));
        Assert.True(store.AddOrUpdate([update]).Success);

        // Act
        var swept = store.SweepExpired(T0.AddMinutes(1), "SWEEPER");

        // Assert
        Assert.Equal(1, swept);
        Assert.Empty(store.GetSnapshot());
    }
}
