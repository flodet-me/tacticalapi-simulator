using System.ComponentModel.DataAnnotations;

namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Settings for the <c>OwnPose</c> service, bound from "Simulator:OwnPose".
///     Read through <c>IOptionsMonitor</c>, so the primary source can be switched
///     while a client is subscribed and it will see the position change under it.
/// </summary>
public sealed class OwnPoseOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":OwnPose";

    /// <summary>
    ///     Which position source is the primary one - the only position
    ///     <c>GetPosition</c> and <c>SubscribePositionChangedEvents</c> ever return,
    ///     per the contract ("the one selected as primary position source by the
    ///     application"). Null - the default - means whichever source reported most
    ///     recently, which is the behaviour that needs no configuration at all to
    ///     look right with a single sensor feeding it.
    /// </summary>
    public string? PrimarySource { get; set; }

    /// <summary>
    ///     How long the primary position stays valid without a fresh update before
    ///     it is reported with <c>is_invalid_or_expired</c> set. The position itself
    ///     is kept - the contract is explicit that "the old position continues to be
    ///     used but is considered outdated", which is what a GNSS fix looks like
    ///     after its owner walks into a building.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PositionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval at which the primary position is re-checked for staleness.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);
}
