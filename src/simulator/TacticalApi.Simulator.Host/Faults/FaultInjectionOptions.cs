using System.ComponentModel.DataAnnotations;
using Grpc.Core;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Host.Faults;

/// <summary>
///     Makes the simulator misbehave on purpose. A simulator's first job is to look
///     like a working server; its second is to let a client prove it survives one
///     that isn't - slow, flaky, rejecting writes, dropping long-lived streams -
///     and none of that could be exercised here before without editing code.
///     Bound from "Simulator:Faults" through <c>IOptionsMonitor</c> like everything
///     else, which is the point: faults can be switched on and off in
///     appsettings.json while a client stays connected, so you can watch it react
///     instead of restarting into a differently-broken server.
///     Every probability is an independent per-call draw in [0, 1]; 0 disables that
///     fault. Nothing here applies at all unless <see cref="Enabled" /> is set, so a
///     stray probability left in a config file can't quietly degrade a normal run.
/// </summary>
public sealed class FaultInjectionOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":Faults";

    /// <summary>Master switch. Off by default - the simulator's normal job is to work.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Seed for the fault RNG. Leave null for a different sequence every run;
    ///     set it to make a flaky run reproducible, which is the difference between
    ///     a fault a client's test suite can assert on and one it can only be
    ///     surprised by.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>Artificial delay added to every RPC before it is handled.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Random extra delay on top of <see cref="Latency" />, drawn per call from
    ///     [0, LatencyJitter]. A constant delay is a network you can plan around; a
    ///     variable one is the network clients actually get.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan LatencyJitter { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Probability that an RPC fails at the gRPC level with
    ///     <see cref="RpcStatusCode" />, i.e. the call itself faults rather than
    ///     returning an unsuccessful response.
    /// </summary>
    [Range(0.0, 1.0)]
    public double RpcErrorProbability { get; set; }

    /// <summary>Status code used by <see cref="RpcErrorProbability" />.</summary>
    public StatusCode RpcStatusCode { get; set; } = StatusCode.Unavailable;

    /// <summary>
    ///     Probability that a write RPC returns a well-formed response whose header
    ///     says <c>success = false</c>. This is the failure clients most often forget
    ///     to handle: the call succeeded, so nothing throws, and the error only exists
    ///     in a field they never read.
    /// </summary>
    [Range(0.0, 1.0)]
    public double ErrorHeaderProbability { get; set; }

    /// <summary>
    ///     Probability that a write is acknowledged with <c>success = true</c> and then
    ///     silently not applied - the server-lost-your-update case, which a client can
    ///     only detect by reconciling against the situation afterwards.
    /// </summary>
    [Range(0.0, 1.0)]
    public double DropWriteProbability { get; set; }

    /// <summary>
    ///     Kills each <c>SubscribeSituationObjectEvents</c> stream this long after it
    ///     was opened. Null (the default) leaves streams alone. Long-lived streams are
    ///     exactly where reconnect logic goes untested, because in development nothing
    ///     ever interrupts them.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "24:00:00")]
    public TimeSpan? StreamAbortAfter { get; set; }
}
