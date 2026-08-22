using Grpc.Core;
using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core.Diagnostics;
using TacticalApi.Simulator.Host.Logging;

namespace TacticalApi.Simulator.Host.Faults;

/// <summary>
///     Decides, per call, which of the configured faults fire. Options are re-read
///     on every decision (<c>IOptionsMonitor</c>), so faults can be turned on and off
///     underneath a running client.
///     Every fault that fires is counted on <c>tacticalapi_faults_injected_total</c>
///     tagged with its kind, so a confusing client-side failure can be traced back to
///     the fault that caused it instead of being mistaken for a real bug.
/// </summary>
public sealed class FaultInjector(
    IOptionsMonitor<FaultInjectionOptions> options,
    SimulatorMetrics metrics,
    ILogger<FaultInjector> logger)
{
    private readonly Lock _randomGate = new();
    private Random? _seeded;
    private int? _seededWith;

    /// <summary>Whether fault injection is switched on at all.</summary>
    public bool IsEnabled => options.CurrentValue.Enabled;

    /// <summary>
    ///     Applies the configured artificial latency, if any. Awaited before the call
    ///     is handled, so it delays the response rather than the work.
    /// </summary>
    public async Task ApplyLatencyAsync(string method, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled) return;

        var delay = settings.Latency;
        if (settings.LatencyJitter > TimeSpan.Zero)
            delay += settings.LatencyJitter * NextDouble(settings);

        if (delay <= TimeSpan.Zero) return;

        Record("latency", method);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Throws the configured gRPC status if this call drew an RPC-level fault.</summary>
    public void ThrowIfRpcFaultDrawn(string method)
    {
        var settings = options.CurrentValue;
        if (!Draw(settings, settings.RpcErrorProbability)) return;

        Record("rpc-error", method);
        throw new RpcException(new Status(settings.RpcStatusCode, "Injected fault (Simulator:Faults)."));
    }

    /// <summary>Whether this write should be answered with an unsuccessful response header.</summary>
    public bool ShouldReturnErrorHeader(string method)
    {
        var settings = options.CurrentValue;
        if (!Draw(settings, settings.ErrorHeaderProbability)) return false;

        Record("error-header", method);
        return true;
    }

    /// <summary>Whether this write should be acknowledged as successful but not applied.</summary>
    public bool ShouldDropWrite(string method)
    {
        var settings = options.CurrentValue;
        if (!Draw(settings, settings.DropWriteProbability)) return false;

        Record("dropped-write", method);
        return true;
    }

    /// <summary>How long a subscription may live before it is aborted, or null to leave it alone.</summary>
    public TimeSpan? StreamLifetime()
    {
        var settings = options.CurrentValue;
        return settings.Enabled ? settings.StreamAbortAfter : null;
    }

    /// <summary>Records that a stream was cut short by the configured lifetime.</summary>
    public void RecordStreamAborted(string peer)
    {
        Record("stream-abort", peer);
    }

    private void Record(string kind, string method)
    {
        metrics.RecordFaultInjected(kind);
        logger.FaultInjected(kind, method);
    }

    private bool Draw(FaultInjectionOptions settings, double probability)
    {
        return settings.Enabled && probability > 0 && NextDouble(settings) < probability;
    }

    /// <summary>
    ///     One RNG for every fault decision. Unseeded runs go through
    ///     <see cref="Random.Shared" />, which is thread-safe and lock-free - this is
    ///     on every RPC's path, so a lock here would show up as contention on exactly
    ///     the calls faults are meant to be measured on. A seeded run accepts that
    ///     lock in exchange for a reproducible fault sequence, which is the whole
    ///     reason to set a seed.
    ///     This injector is a singleton, so the seeded sequence is process-wide and
    ///     the same config always replays the same faults.
    /// </summary>
    // S2245 (use a cryptographically strong RNG) doesn't apply: this RNG decides
    // whether to make a test server misbehave. Nothing about it is a secret, and a
    // predictable sequence is a feature here - it's exactly what Seed asks for.
#pragma warning disable S2245
    private double NextDouble(FaultInjectionOptions settings)
    {
        if (settings.Seed is null) return Random.Shared.NextDouble();

        lock (_randomGate)
        {
            if (_seeded is null || _seededWith != settings.Seed)
            {
                _seeded = new Random(settings.Seed.Value);
                _seededWith = settings.Seed;
            }

            return _seeded.NextDouble();
        }
    }
#pragma warning restore S2245
}
