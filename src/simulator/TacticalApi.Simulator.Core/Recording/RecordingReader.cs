using Microsoft.Extensions.Logging;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     Reads a ".jsonl" recording back into <see cref="RecordedFrame" />s.
///     Unreadable lines are skipped with a warning instead of failing the load: the
///     last line of a recording whose process was killed mid-write is routinely a
///     partial one, and losing the final frame is a far better outcome than losing
///     the recording.
/// </summary>
public static class RecordingReader
{
    /// <summary>Reads every frame of the recording at <paramref name="path" />, in file order.</summary>
    public static async Task<IReadOnlyList<RecordedFrame>> ReadAllAsync(
        string path, ILogger logger, CancellationToken cancellationToken = default)
    {
        var frames = new List<RecordedFrame>();
        var lineNumber = 0;

        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var frame = RecordingFormat.TryRead(line);
            if (frame is null) logger.RecordingFrameSkipped(lineNumber, path);
            else frames.Add(frame);
        }

        return frames;
    }
}
