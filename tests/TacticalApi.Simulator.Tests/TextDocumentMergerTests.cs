using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="TextDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/TextDocumentMerger.cs).
/// </summary>
public sealed class TextDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new TextDocumentMerger();
        var create = new UpdateSituationObject
        {
            TextDocument = new UpdateTextDocument
            {
                Identity = new Identity { StringIdentity = "d1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Doc X" },
                Content = new UpdatePropertyString { Content = "<b>hello</b>" },
                PlainContent = new UpdatePropertyString { Content = "hello" },
                MessageCategory = new UpdatePropertyMessageCategory { Content = MessageCategoryType.Warning },
                MessagePrecedence = new UpdatePropertyMessagePrecedence { Content = MessagePrecedenceType.Flash },
                ForeignKey = new UpdatePropertyIdentity
                {
                    Source = "NWS",
                    Content = new Identity { StringIdentity = "alert-1" }
                }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - only content changes, name/category/precedence must survive.
        var update = new UpdateSituationObject
        {
            TextDocument = new UpdateTextDocument
            {
                Identity = new Identity { StringIdentity = "d1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                Content = new UpdatePropertyString { Content = "<b>updated</b>" },
                PlainContent = new UpdatePropertyString { Content = "updated" }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Doc X", merged.TextDocument.Name.Content);
        Assert.Equal("updated", merged.TextDocument.PlainContent.Content);
        Assert.Equal(MessageCategoryType.Warning, merged.TextDocument.MessageCategory.Content);
        Assert.Equal(MessagePrecedenceType.Flash, merged.TextDocument.MessagePrecedence.Content);
        var foreignKey = Assert.Contains(
            "NWS", (IDictionary<string, DataPropertyIdentity>)merged.TextDocument.ForeignKeys);
        Assert.Equal("alert-1", foreignKey.Content.StringIdentity);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal("hello", created.TextDocument.PlainContent.Content);
    }
}
