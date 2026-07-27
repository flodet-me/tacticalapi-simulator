using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>Outcome of an ingest batch, mapping 1:1 onto a ResponseHeader.</summary>
public readonly record struct IngestResult(bool Success, string? ErrorMessage)
{
    /// <summary>A successful ingest with no error message.</summary>
    public static IngestResult Ok { get; } = new(true, null);

    /// <summary>A failed ingest carrying the given error message.</summary>
    public static IngestResult Fail(string message)
    {
        return new IngestResult(false, message);
    }

    /// <summary>Converts this result into the gRPC <see cref="ResponseHeader" /> wire shape.</summary>
    public ResponseHeader ToHeader()
    {
        return new ResponseHeader
        {
            Success = Success,
            ErrorMessage = ErrorMessage
        };
    }
}
