using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SymbolMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/SymbolMerger.cs).
/// </summary>
public sealed class SymbolMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_MergesForeignKeyByReportingSource()
    {
        // S125 false positive: the two lines below are prose, not commented-out code.
#pragma warning disable S125
        // Arrange: the update model carries one foreign key (identity + source);
        // the stored model keeps a dictionary keyed by that source (PropertyMerge.ForeignKey).
#pragma warning restore S125
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new SymbolMerger();
        var create = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "s1" },
                Reporter = reporter,
                ReportingTime = time,
                ForeignKey = new UpdatePropertyIdentity
                {
                    Source = "AIS",
                    Content = new Identity { StringIdentity = "mmsi:123456" }
                }
            }
        };

        // Act
        var created = merger.Merge(null, create);

        // Assert
        var foreignKey = Assert.Contains("AIS", (IDictionary<string, DataPropertyIdentity>)created.Symbol.ForeignKeys);
        Assert.Equal("mmsi:123456", foreignKey.Content.StringIdentity);
    }

    [Fact]
    public void Merge_IgnoresForeignKeyUpdateWithSameOrOlderReportingTime()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new SymbolMerger();
        var created = merger.Merge(null, new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "s1" },
                Reporter = reporter,
                ReportingTime = time,
                ForeignKey = new UpdatePropertyIdentity
                {
                    Source = "AIS",
                    Content = new Identity { StringIdentity = "mmsi:123456" }
                }
            }
        });

        // Act: same reporting time as the stored foreign key entry - not strictly newer.
        var merged = merger.Merge(created, new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "s1" },
                Reporter = reporter,
                ReportingTime = time,
                ForeignKey = new UpdatePropertyIdentity
                {
                    Source = "AIS",
                    Content = new Identity { StringIdentity = "mmsi:999999" }
                }
            }
        });

        // Assert
        var foreignKey = Assert.Contains("AIS", (IDictionary<string, DataPropertyIdentity>)merged.Symbol.ForeignKeys);
        Assert.Equal("mmsi:123456", foreignKey.Content.StringIdentity);
    }
}
