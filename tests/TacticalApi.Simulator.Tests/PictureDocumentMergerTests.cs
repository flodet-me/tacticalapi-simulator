using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="PictureDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/PictureDocumentMerger.cs).
/// </summary>
public sealed class PictureDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new PictureDocumentMerger();
        var create = new UpdateSituationObject
        {
            PictureDocument = new UpdatePictureDocument
            {
                Identity = new Identity { StringIdentity = "p1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Recon Photo" },
                PictureData = new UpdatePropertyByteArray
                {
                    Content = ByteString.CopyFromUtf8("full-res"),
                    Type = "image/png"
                },
                LowResPictureData = new UpdatePropertyByteArray
                {
                    Content = ByteString.CopyFromUtf8("thumb"),
                    Type = "image/png"
                },
                DirectionOfView = new UpdatePropertyInt { Content = 90 },
                FocalLength = new UpdatePropertyInt { Content = 35 }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - only direction of view changes, picture data must survive.
        var update = new UpdateSituationObject
        {
            PictureDocument = new UpdatePictureDocument
            {
                Identity = new Identity { StringIdentity = "p1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                DirectionOfView = new UpdatePropertyInt { Content = 180 }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Recon Photo", merged.PictureDocument.Name.Content);
        Assert.Equal(ByteString.CopyFromUtf8("full-res"), merged.PictureDocument.PictureData.Content);
        Assert.Equal("image/png", merged.PictureDocument.LowResPictureData.Type);
        Assert.Equal(35, merged.PictureDocument.FocalLength.Content);
        Assert.Equal(180, merged.PictureDocument.DirectionOfView.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal(90, created.PictureDocument.DirectionOfView.Content);
    }
}
