using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="NatoMessageDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/NatoMessageDocumentMerger.cs).
/// </summary>
public sealed class NatoMessageDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new NatoMessageDocumentMerger();
        var create = new UpdateSituationObject
        {
            NatoMessageDocument = new UpdateNatoMessageDocument
            {
                Identity = new Identity { StringIdentity = "n1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "MTF Report" },
                MtfMessageData = new UpdatePropertyString { Content = "MSGID/OPREP/1//" },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Operational },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Priority }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - only the MTF payload changes, name/category must survive.
        var update = new UpdateSituationObject
        {
            NatoMessageDocument = new UpdateNatoMessageDocument
            {
                Identity = new Identity { StringIdentity = "n1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                MtfMessageData = new UpdatePropertyString { Content = "MSGID/OPREP/2//" }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("MTF Report", merged.NatoMessageDocument.Name.Content);
        Assert.Equal("MSGID/OPREP/2//", merged.NatoMessageDocument.MtfMessageData.Content);
        Assert.Equal(MessageCategoryType.Operational, merged.NatoMessageDocument.MessageCategory.Content);
        Assert.Equal(MessagePrecedenceType.Priority, merged.NatoMessageDocument.MessagePrecedence.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal("MSGID/OPREP/1//", created.NatoMessageDocument.MtfMessageData.Content);
    }
}
