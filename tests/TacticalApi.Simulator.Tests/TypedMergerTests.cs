using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

public sealed class TypedMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static (Identity Reporter, Timestamp Time) Meta()
    {
        return (new Identity { StringIdentity = TestHelpers.TestReporterId }, Timestamp.FromDateTimeOffset(T0));
    }

    [Fact]
    public void RouteMerger_CreatesAndPartiallyUpdates()
    {
        var (reporter, time) = Meta();
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

        // Partial update: change width only, name must survive.
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

        Assert.Equal("Route X", merged.Route.Name.Content);
        Assert.Equal(5, merged.Route.LineWidth.Content);
        Assert.Equal(RouteType.MainSupplyRoute, merged.Route.RouteType.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal(2, created.Route.LineWidth.Content);
    }

    [Fact]
    public void OrganizationUnitMerger_KeepsSubordinateReferences()
    {
        var (reporter, time) = Meta();
        var merger = new OrganizationUnitMerger();
        var refs = new UpdatePropertyReferences();
        refs.Contents.Add(new Identity { StringIdentity = "sub1" });

        var created = merger.Merge(null, new UpdateSituationObject
        {
            OrganizationUnit = new UpdateOrganizationUnit
            {
                Identity = new Identity { StringIdentity = "u1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Unit" },
                UnitDesignation = new UpdatePropertyUnitDesignation { Content = UnitDesignation.Platoon },
                SubordinatedOrganizationUnitCollection = refs
            }
        });

        var identity = Assert.Single(created.OrganizationUnit.SubordinatedOrganizationUnitCollection.Contents);
        Assert.Equal("sub1", identity.StringIdentity);
        Assert.Equal(UnitDesignation.Platoon, created.OrganizationUnit.UnitDesignation.Content);
    }

    [Fact]
    public void ActionEventMerger_MapsThreatAndType()
    {
        var (reporter, time) = Meta();
        var merger = new ActionEventMerger();

        var created = merger.Merge(null, new UpdateSituationObject
        {
            ActionEvent = new UpdateActionEvent
            {
                Identity = new Identity { StringIdentity = "e1" },
                Reporter = reporter,
                ReportingTime = time,
                ActionEventType = new UpdatePropertyActionEvent { Content = ActionEventType.SniperAttack },
                ThreatLevel = new UpdatePropertyInt { Content = 4 }
            }
        });

        Assert.Equal(ActionEventType.SniperAttack, created.ActionEvent.ActionEventType.Content);
        Assert.Equal(4, created.ActionEvent.ThreatLevel.Content);
    }
}
