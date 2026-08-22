using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Recording;

/// <summary>
///     One recorded moment of situation traffic: the batch of writes that crossed
///     the wire together, plus how far into the recording it happened.
///     <see cref="Offset" /> is relative to the first frame, not wall-clock, so a
///     recording replays identically whenever it's started.
///     A frame carries updates, deletes, or both - matching the two write RPCs of
///     the contract - because a source is free to emit either in one cycle.
/// </summary>
/// <param name="Sequence">1-based position in the recording; used for diagnostics only.</param>
/// <param name="Offset">Time since the first frame of the recording.</param>
/// <param name="Updates">AddOrUpdateSituationObjects payload of this frame.</param>
/// <param name="Deletes">DeleteSituationObjects payload of this frame.</param>
public sealed record RecordedFrame(
    long Sequence,
    TimeSpan Offset,
    IReadOnlyList<UpdateSituationObject> Updates,
    IReadOnlyList<DeleteSituationObject> Deletes)
{
    /// <summary>Total number of objects carried by this frame.</summary>
    public int ObjectCount => Updates.Count + Deletes.Count;
}
