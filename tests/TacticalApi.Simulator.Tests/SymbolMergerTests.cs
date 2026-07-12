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
        // Arrange: the update model carries one foreign key (identity + source);
        // the stored model keeps a dictionary keyed by that source (PropertyMerge.ForeignKey).
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
}
