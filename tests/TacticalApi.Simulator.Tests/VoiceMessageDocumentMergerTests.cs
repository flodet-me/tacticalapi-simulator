using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Merging;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for <see cref="VoiceMessageDocumentMerger" />
///     (src/TacticalApi.Simulator.Core/Merging/VoiceMessageDocumentMerger.cs).
/// </summary>
public sealed class VoiceMessageDocumentMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merge_CreatesAndPartiallyUpdates()
    {
        // Arrange
        var (reporter, time) = TestHelpers.Meta(T0);
        var merger = new VoiceMessageDocumentMerger();
        var create = new UpdateSituationObject
        {
            VoiceMessageDocument = new UpdateVoiceMessageDocument
            {
                Identity = new Identity { StringIdentity = "v1" },
                Reporter = reporter,
                ReportingTime = time,
                Name = new UpdatePropertyString { Content = "Radio Check" },
                SoundFile = new UpdatePropertyByteArray
                {
                    Content = ByteString.CopyFromUtf8("audio-bytes-v1"),
                    Type = "audio/wav"
                }
            }
        };
        var created = merger.Merge(null, create);

        // Act: partial update - replace the sound file, name must survive.
        var update = new UpdateSituationObject
        {
            VoiceMessageDocument = new UpdateVoiceMessageDocument
            {
                Identity = new Identity { StringIdentity = "v1" },
                Reporter = reporter,
                ReportingTime = Timestamp.FromDateTimeOffset(T0.AddSeconds(1)),
                SoundFile = new UpdatePropertyByteArray
                {
                    Content = ByteString.CopyFromUtf8("audio-bytes-v2"),
                    Type = "audio/wav"
                }
            }
        };
        var merged = merger.Merge(created, update);

        // Assert
        Assert.Equal("Radio Check", merged.VoiceMessageDocument.Name.Content);
        Assert.Equal(ByteString.CopyFromUtf8("audio-bytes-v2"), merged.VoiceMessageDocument.SoundFile.Content);
        // Copy-on-write: the original instance is untouched.
        Assert.Equal(ByteString.CopyFromUtf8("audio-bytes-v1"), created.VoiceMessageDocument.SoundFile.Content);
    }
}
