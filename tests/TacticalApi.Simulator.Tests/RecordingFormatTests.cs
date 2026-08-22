using Google.Protobuf.WellKnownTypes;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Recording;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for the recording file format
///     (src/simulator/TacticalApi.Simulator.Core/Recording/RecordingFormat.cs).
/// </summary>
public sealed class RecordingFormatTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ProducesExactlyOneLine()
    {
        // Arrange - the whole format rests on one frame per line; a formatter switched
        // into multi-line mode would break every reader without failing to serialize.
        var frame = new RecordedFrame(1, TimeSpan.FromSeconds(3),
            [TestHelpers.SymbolUpdate("track-1", T0, "ALPHA")], []);

        // Act
        var line = RecordingFormat.Write(frame);

        // Assert
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void WriteThenRead_RoundTripsUpdates()
    {
        // Arrange
        var frame = new RecordedFrame(7, TimeSpan.FromMilliseconds(1234),
            [TestHelpers.SymbolUpdate("track-1", T0, "ALPHA", 53.1, 8.8)], []);

        // Act
        var parsed = RecordingFormat.TryRead(RecordingFormat.Write(frame));

        // Assert
        Assert.NotNull(parsed);
        Assert.Equal(7, parsed.Sequence);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), parsed.Offset);
        Assert.Equal("ALPHA", parsed.Updates[0].Symbol.Name.Content);
        Assert.Equal(53.1, parsed.Updates[0].Symbol.Location.Content.Point.GeoPoint.LatitudeCoordinate);
    }

    [Fact]
    public void WriteThenRead_RoundTripsDeletes()
    {
        // Arrange
        var delete = new DeleteSituationObject
        {
            Identity = new Identity { StringIdentity = "track-1" },
            Reporter = new Identity { StringIdentity = "TEST" },
            ReportingTime = Timestamp.FromDateTimeOffset(T0)
        };

        // Act
        var parsed = RecordingFormat.TryRead(
            RecordingFormat.Write(new RecordedFrame(1, TimeSpan.Zero, [], [delete])));

        // Assert
        Assert.NotNull(parsed);
        Assert.Empty(parsed.Updates);
        Assert.Equal("track-1", parsed.Deletes[0].Identity.StringIdentity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"seq\":1,\"offsetMs\":0,\"upd")]
    [InlineData("not json at all")]
    [InlineData("{\"seq\":1,\"updates\":[{\"symbol\":{\"nope\":1}}]}")]
    public void TryRead_ReturnsNullForUnreadableLines(string line)
    {
        // A recording whose process was killed mid-write routinely ends in a partial
        // line; losing that frame must not cost the reader the whole file.
        Assert.Null(RecordingFormat.TryRead(line));
    }

    [Fact]
    public void TryRead_ToleratesAMissingSequenceOrOffset()
    {
        // Arrange - hand-edited recordings are an explicitly supported workflow.
        const string line = "{\"updates\":[]}";

        // Act
        var parsed = RecordingFormat.TryRead(line);

        // Assert
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.Sequence);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }
}
