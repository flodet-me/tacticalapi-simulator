using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     The single write path into the simulated situation. Both the gRPC
///     AddOrUpdate/Delete RPCs and every simulation data source go through this
///     interface with the plain TacticalAPI update model - a source is therefore
///     indistinguishable from an external client writing to the interface.
/// </summary>
public interface ISituationIngest
{
    /// <summary>Applies add/update messages. Returns per-batch success.</summary>
    public IngestResult AddOrUpdate(IReadOnlyList<UpdateSituationObject> updates);

    /// <summary>Marks objects as deleted.</summary>
    public IngestResult Delete(IReadOnlyList<DeleteSituationObject> deletes);
}
