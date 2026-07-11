using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;

namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Everything performance-related is tunable here instead of being hard-coded.
///     Read through <c>IOptionsMonitor</c>; new subscriptions and source cycles
///     pick up changed values immediately.
/// </summary>
public sealed class PerformanceOptions
{
    /// <summary>
    ///     Capacity of the per-subscriber event channel. Larger values absorb
    ///     bursts at the cost of memory per subscriber.
    /// </summary>
    [Range(1, 1_000_000)]
    public int SubscriberChannelCapacity { get; set; } = 4096;

    /// <summary>
    ///     What to do when a subscriber cannot keep up:
    ///     DropOldest (default) keeps the stream fresh - fine for state-based
    ///     updates because a newer full object supersedes the old one.
    ///     Wait applies backpressure to the publisher instead.
    /// </summary>
    public BoundedChannelFullMode SubscriberChannelFullMode { get; set; } = BoundedChannelFullMode.DropOldest;

    /// <summary>
    ///     Number of situation objects packed into a single streaming response
    ///     (initial snapshot and live events). Bigger batches mean fewer gRPC
    ///     messages but higher per-message latency.
    /// </summary>
    [Range(1, 10_000)]
    public int StreamBatchSize { get; set; } = 256;

    /// <summary>Maximum inbound gRPC message size in megabytes.</summary>
    [Range(1, 1024)]
    public int MaxReceiveMessageSizeMb { get; set; } = 16;

    /// <summary>
    ///     Hard cap on stored situation objects; ingest beyond this fails with an
    ///     error header. Guards runtime memory since there is no database.
    /// </summary>
    [Range(1, 10_000_000)]
    public int MaxSituationObjects { get; set; } = 100_000;
}
