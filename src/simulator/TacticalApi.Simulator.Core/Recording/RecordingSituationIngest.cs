using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Core.Ingest;
using TacticalApi.Simulator.Core.Logging;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     Decorates the real <see cref="ISituationIngest" /> with a recording sink:
///     every batch is appended to the recording and then forwarded to the inner
///     ingest unchanged.
///     Recording on the way out rather than off the event stream is what makes this
///     lossless - the frame written is exactly the <see cref="UpdateSituationObject" />
///     that went on the wire, not a reconstruction of it from stored state - and it
///     works for every source ever added without any of them knowing about it.
///     A failing recording never fails the ingest: losing a recording is annoying,
///     but taking the adapter's actual job down with it would be worse.
/// </summary>
public sealed class RecordingSituationIngest : ISituationIngest, IAsyncDisposable
{
    private readonly ISituationIngest _inner;
    private readonly ILogger<RecordingSituationIngest> _logger;
    private readonly string _path;
    private RecordingWriter? _writer;

    /// <summary>Wraps <paramref name="inner" />, opening the configured recording file.</summary>
    public RecordingSituationIngest(
        ISituationIngest inner,
        IOptions<RecordingOptions> options,
        TimeProvider time,
        ILogger<RecordingSituationIngest> logger)
    {
        _inner = inner;
        _logger = logger;
        _path = options.Value.Path;
        _writer = new RecordingWriter(_path, time, logger);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_writer is null) return;

        _logger.RecordingClosed(_path, _writer.FrameCount);
        await _writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
    }

    /// <inheritdoc/>
    public async Task<IngestResult> AddOrUpdateAsync(
        IReadOnlyList<UpdateSituationObject> updates, CancellationToken cancellationToken = default)
    {
        await RecordAsync(updates, [], cancellationToken).ConfigureAwait(false);
        return await _inner.AddOrUpdateAsync(updates, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IngestResult> DeleteAsync(
        IReadOnlyList<DeleteSituationObject> deletes, CancellationToken cancellationToken = default)
    {
        await RecordAsync([], deletes, cancellationToken).ConfigureAwait(false);
        return await _inner.DeleteAsync(deletes, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        IReadOnlyList<UpdateSituationObject> updates,
        IReadOnlyList<DeleteSituationObject> deletes,
        CancellationToken cancellationToken)
    {
        var writer = _writer;
        if (writer is null) return;

        try
        {
            await writer.WriteAsync(updates, deletes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.RecordingFailed(ex, _path);
            _writer = null;
        }
        catch (ObjectDisposedException ex)
        {
            // The host is shutting down underneath an in-flight cycle.
            _logger.RecordingFailed(ex, _path);
            _writer = null;
        }
    }
}
