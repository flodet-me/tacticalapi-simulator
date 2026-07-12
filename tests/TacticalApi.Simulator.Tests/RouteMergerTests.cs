using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="RouteMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/RouteMerger.cs).
/// </summary>
public sealed class RouteMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new RouteMerger();
        var create = new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Route X" },
                LineWidth = new UpdatePropertyInt { Content = 2 },
                RouteType = new UpdatePropertyRouteType { Content = RouteType.MainSupplyRoute }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - change width only, name must survive.
        var update = new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                LineWidth = new UpdatePropertyInt { Content = 5 }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Route X", merged.Route.Name.Content);
        Assert.Equal(5, merged.Route.LineWidth.Content);
        Assert.Equal(RouteType.MainSupplyRoute, merged.Route.RouteType.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal(2, created.Route.LineWidth.Content);
    }
}
