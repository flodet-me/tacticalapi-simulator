using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="SketchDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/SketchDocumentMerger.cs).
/// </summary>
public sealed class SketchDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new SketchDocumentMerger();
        var location = new UpdatePropertyLocation
        {
            Content = new SymbolLocation
            {
                Point = new Point
                {
                    GeoPoint = new GeoPoint { LatitudeCoordinate = 52.5, LongitudeCoordinate = 13.4 }
                }
            }
        };
        var create = new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = "sk1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Sketch X" },
                Location = location,
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Normal }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - only name changes, location must survive.
        var update = new UpdateSituationObject
        {
            SketchDocument = new UpdateSketchDocument
            {
                Identity = new Identity { StringIdentity = "sk1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                Name = new UpdatePropertyString { Content = "Sketch X (renamed)" }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Sketch X (renamed)", merged.SketchDocument.Name.Content);
        Assert.Equal(52.5, merged.SketchDocument.Location.Content.Point.GeoPoint.LatitudeCoordinate);
        Assert.Equal(MessageCategoryType.Normal, merged.SketchDocument.MessageCategory.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal("Sketch X", created.SketchDocument.Name.Content);
    }
}
