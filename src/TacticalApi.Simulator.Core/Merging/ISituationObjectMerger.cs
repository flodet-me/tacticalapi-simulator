using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
/// Merges one <see cref="UpdateSituationObject"/> variant into the stored
/// <see cref="SituationObject"/>. One implementation per oneof case; register
/// additional implementations in DI to support more object types - the store
/// discovers them by <see cref="HandledCase"/>.
/// </summary>
public interface ISituationObjectMerger
{
    public UpdateSituationObject.TypeOneofCase HandledCase { get; }

    /// <summary>Identity of the object addressed by this update.</summary>
    public Identity? GetIdentity(UpdateSituationObject update);

    /// <summary>Reporting time of the update (used for last-write-wins).</summary>
    public Google.Protobuf.WellKnownTypes.Timestamp? GetReportingTime(UpdateSituationObject update);

    /// <summary>
    /// Returns a NEW SituationObject representing <paramref name="current"/>
    /// (may be null for a create) with the update applied. Must not mutate
    /// <paramref name="current"/> - the store relies on copy-on-write so
    /// published instances are effectively immutable.
    /// </summary>
    public SituationObject Merge(SituationObject? current, UpdateSituationObject update);
}
