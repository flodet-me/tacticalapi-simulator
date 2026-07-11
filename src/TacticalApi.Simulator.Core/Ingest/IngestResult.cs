using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>Outcome of an ingest batch, mapping 1:1 onto a ResponseHeader.</summary>
public readonly record struct IngestResult(bool Success, string? ErrorMessage)
{
    public static IngestResult Ok { get; } = new(true, null);

    public static IngestResult Fail(string message)
    {
        return new IngestResult(false, message);
    }

    public ResponseHeader ToHeader()
    {
        return new ResponseHeader
        {
            Success = Success,
            ErrorMessage = ErrorMessage
        };
    }
}
