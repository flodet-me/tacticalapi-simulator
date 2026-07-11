using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Configuration;
using Xunit;

namespace TacticalApi.Simulator.Tests;

public sealed class SituationStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddOrUpdate_CreatesNewSymbol()
    {
        var store = TestHelpers.CreateStore();

        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, name: "ALPHA", latitude: 53.0, longitude: 8.8)]);

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
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, name: "ALPHA", latitude: 53.0, longitude: 8.8)]);

        // Second update moves the track but does not touch the name.
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(5), latitude: 54.0, longitude: 9.0)]);

        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("ALPHA", obj.Symbol.Name.Content);
        Assert.Equal(54.0, obj.Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void AddOrUpdate_IgnoresStaleUpdates()
    {
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0, name: "NEW")]);

        var result = store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(-30), name: "STALE")]);

        Assert.True(result.Success);
        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("NEW", obj.Symbol.Name.Content);
    }

    [Fact]
    public void AddOrUpdate_FailsForMissingIdentity()
    {
        var store = TestHelpers.CreateStore();
        var update = new UpdateSituationObject { Symbol = new UpdateSymbol() };

        var result = store.AddOrUpdate([update]);

        Assert.False(result.Success);
    }

    [Fact]
    public void AddOrUpdate_FailsForUnsupportedType()
    {
        var store = TestHelpers.CreateStore();
        var update = new UpdateSituationObject { Route = new UpdateRoute() };

        var result = store.AddOrUpdate([update]);

        Assert.False(result.Success);
        Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddOrUpdate_EnforcesMaxObjectLimit()
    {
        var options = new SimulatorOptions();
        options.Performance.MaxSituationObjects = 1;
        var store = TestHelpers.CreateStore(options);

        Assert.True(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]).Success);
        Assert.False(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-2", T0)]).Success);

        // Updating an existing object must still work at the limit.
        Assert.True(store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0.AddSeconds(1), name: "STILL-OK")]).Success);
    }

    [Fact]
    public void Delete_MarksObjectDeleted_AndSnapshotExcludesIt()
    {
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([TestHelpers.SymbolUpdate("track-1", T0)]);

        var result = store.Delete([TestHelpers.Delete("track-1", T0.AddSeconds(1))]);

        Assert.True(result.Success);
        Assert.Empty(store.GetSnapshot());
    }

    [Fact]
    public void Delete_UnknownObject_IsNoOp()
    {
        var store = TestHelpers.CreateStore();

        var result = store.Delete([TestHelpers.Delete("ghost", T0)]);

        Assert.True(result.Success);
    }

    [Fact]
    public void SweepExpired_DeletesExpiredObjects()
    {
        var store = TestHelpers.CreateStore();
        store.AddOrUpdate([
            TestHelpers.SymbolUpdate("expired", T0, expiry: T0.AddSeconds(10)),
            TestHelpers.SymbolUpdate("alive", T0, expiry: T0.AddHours(1)),
        ]);

        var swept = store.SweepExpired(T0.AddMinutes(1), "SWEEPER");

        Assert.Equal(1, swept);
        var obj = Assert.Single(store.GetSnapshot());
        Assert.Equal("alive", obj.Symbol.Identity.StringIdentity);
    }
}
