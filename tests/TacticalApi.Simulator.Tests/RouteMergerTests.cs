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

    [Fact]
    public void Merge_IgnoresUpdateWithSameOrOlderReportingTime()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new RouteMerger();
        var created = merger.Merge(null, new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = time,
                LineWidth = new UpdatePropertyInt { Content = 2 }
            }
        });

        // Act: same reporting time, then an older one - neither is strictly newer.
        var sameTime = merger.Merge(created, new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = time,
                LineWidth = new UpdatePropertyInt { Content = 99 }
            }
        });
        var olderTime = merger.Merge(created, new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(-1)),
                LineWidth = new UpdatePropertyInt { Content = 99 }
            }
        });

        // Assert
        Assert.Equal(2, sameTime.Route.LineWidth.Content);
        Assert.Equal(2, olderTime.Route.LineWidth.Content);
    }

    [Fact]
    public void Merge_KeepsPropertyMetadataWhenContentUnchangedDespiteNewerTime()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new RouteMerger();
        var created = merger.Merge(null, new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = time,
                LineWidth = new UpdatePropertyInt { Content = 2 }
            }
        });

        // Act: strictly newer reporting time, but the same LineWidth value.
        var merged = merger.Merge(created, new UpdateSituationObject
        {
            Route = new UpdateRoute
            {
                Identity = new Identity { StringIdentity = "r1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddMinutes(1)),
                LineWidth = new UpdatePropertyInt { Content = 2 }
            }
        });

        // Assert: value is unchanged and its CreationMetaData was NOT bumped to
        // the newer time - an unchanged property is left completely untouched.
        Assert.Equal(2, merged.Route.LineWidth.Content);
        Assert.Equal(time, merged.Route.LineWidth.CreationMetaData.CreationTime);
    }
}
