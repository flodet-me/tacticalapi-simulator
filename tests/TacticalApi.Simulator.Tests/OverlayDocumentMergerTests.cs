using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="OverlayDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/OverlayDocumentMerger.cs).
/// </summary>
public sealed class OverlayDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_MaterializesNestedSituationObjects()
    {
        // Arrange: the update carries a nested UpdateSymbol; OverlayDocumentMerger
        // must materialize it into a full SituationObject via NestedObjectMaterializer.
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new OverlayDocumentMerger();
        var nestedSymbol = new UpdateSituationObject
        {
            Symbol = new UpdateSymbol
            {
                Identity = new Identity { StringIdentity = "nested1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Nested Marker" }
            }
        };
        var overlayData = new UpdatePropertySituationObjects();
        overlayData.Contents.Add(nestedSymbol);
        var create = new UpdateSituationObject
        {
            OverlayDocument = new UpdateOverlayDocument
            {
                Identity = new Identity { StringIdentity = "o1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Overlay X" },
                Tag = new UpdatePropertyString { Content = "OV" },
                OverlayData = overlayData
            }
        };

        // Act
        var created = merger.Merge(null, create);

        // Assert
        Assert.Equal("Overlay X", created.OverlayDocument.Name.Content);
        Assert.Equal("OV", created.OverlayDocument.Tag.Content);
        var nested = Assert.Single(created.OverlayDocument.OverlayData.Contents);
        Assert.Equal(SituationObject.TypeOneofCase.Symbol, nested.TypeCase);
        Assert.Equal("nested1", nested.Symbol.Identity.StringIdentity);
        Assert.Equal("Nested Marker", nested.Symbol.Name.Content);
    }
}
