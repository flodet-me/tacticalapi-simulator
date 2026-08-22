using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     The on-disk recording format: newline-delimited JSON, one
///     <see cref="RecordedFrame" /> per line, matching the ".jsonl" convention the
///     file log sink already uses here.
///     Each line is a small hand-written envelope (sequence, offset) wrapping the
///     objects in their canonical protobuf JSON encoding - i.e. still the contract's
///     own model, not a re-modelled copy of it, so a recording stays readable,
///     greppable and hand-editable, and `jq` is a perfectly good recording editor.
///     Line-oriented rather than one big JSON array so a recording that was cut off
///     mid-run (killed process, full disk) is still completely usable up to its last
///     whole line.
/// </summary>
public static class RecordingFormat
{
    private const string SequenceProperty = "seq";
    private const string OffsetProperty = "offsetMs";
    private const string UpdatesProperty = "updates";
    private const string DeletesProperty = "deletes";

    // Protobuf JSON, not System.Text.Json's view of the generated types: only the
    // canonical formatter round-trips oneofs, well-known types and enums the way
    // every other protobuf tool expects.
    // JsonFormatter.Default, deliberately: any Settings.WithIndentation(...) - even
    // with an empty indent string - switches the formatter into multi-line mode, and
    // a frame spread over several lines would break the one-frame-per-line contract
    // this whole format rests on.
    private static readonly JsonFormatter Formatter = JsonFormatter.Default;
    private static readonly JsonParser Parser = JsonParser.Default;

    /// <summary>Serializes one frame to a single JSON line (no trailing newline).</summary>
    public static string Write(RecordedFrame frame)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(SequenceProperty, frame.Sequence);
            writer.WriteNumber(OffsetProperty, (long)frame.Offset.TotalMilliseconds);
            WriteMessages(writer, UpdatesProperty, frame.Updates);
            WriteMessages(writer, DeletesProperty, frame.Deletes);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    ///     Parses one JSON line back into a frame, or returns null if the line isn't
    ///     a readable frame (a truncated last line, or hand-editing gone wrong) -
    ///     callers skip such lines rather than abandoning the whole recording.
    /// </summary>
    public static RecordedFrame? TryRead(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var sequence = root.TryGetProperty(SequenceProperty, out var seq) ? seq.GetInt64() : 0;
            var offsetMs = root.TryGetProperty(OffsetProperty, out var off) ? off.GetInt64() : 0;

            return new RecordedFrame(
                sequence,
                TimeSpan.FromMilliseconds(offsetMs),
                ReadMessages<UpdateSituationObject>(root, UpdatesProperty),
                ReadMessages<DeleteSituationObject>(root, DeletesProperty));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
        catch (InvalidJsonException)
        {
            // Thrown by the protobuf JsonParser (not System.Text.Json) for a line
            // whose envelope parses but whose embedded object doesn't.
            return null;
        }
    }

    private static void WriteMessages<T>(Utf8JsonWriter writer, string property, IReadOnlyList<T> messages)
        where T : IMessage
    {
        if (messages.Count == 0) return;

        writer.WriteStartArray(property);
        foreach (var message in messages) writer.WriteRawValue(Formatter.Format(message));
        writer.WriteEndArray();
    }

    private static List<T> ReadMessages<T>(JsonElement root, string property) where T : IMessage, new()
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return [];

        var result = new List<T>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray()) result.Add(Parser.Parse<T>(element.GetRawText()));

        return result;
    }
}
