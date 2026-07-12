using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="OrganizationUnitMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/OrganizationUnitMerger.cs).
/// </summary>
public sealed class OrganizationUnitMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_KeepsSubordinateReferences()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new OrganizationUnitMerger();
        var refs = new UpdatePropertyReferences();
        refs.Contents.Add(new Identity { StringIdentity = "sub1" });

        // Act
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

        // Assert
        var identity = Assert.Single(created.OrganizationUnit.SubordinatedOrganizationUnitCollection.Contents);
        Assert.Equal("sub1", identity.StringIdentity);
        Assert.Equal(UnitDesignation.Platoon, created.OrganizationUnit.UnitDesignation.Content);
    }
}
