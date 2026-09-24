namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Every check the suite knows about, across all three services of the
///     contract, in the order they are run and reported.
///     Grouped by service rather than interleaved because that is how a failure gets
///     read: an implementation is normally strong on one service and weak on
///     another - typically it has <c>Situation</c> and has only just grown
///     <c>BlueForceTracking</c> - and a report that keeps them together says that at
///     a glance.
/// </summary>
public static class TacticalApiContractChecks
{
    /// <summary>Situation, then BlueForceTracking, then OwnPose.</summary>
    public static IReadOnlyList<ConformanceCheck> All { get; } =
    [
        .. SituationContractChecks.All,
        .. BlueForceContractChecks.All,
        .. OwnPoseContractChecks.All
    ];
}
