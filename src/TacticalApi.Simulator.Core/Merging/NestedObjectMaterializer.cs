using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.Core.Merging;

/// <summary>
///     Converts nested UpdateSituationObjects (e.g. inside overlays) into full
///     SituationObjects by running them through the shared, stateless mergers.
/// </summary>
internal static class NestedObjectMaterializer
{
    // All mergers are stateless, so static instances are safe. Overlay-in-
    // overlay recursion is supported because OverlayDocumentMerger calls back
    // into this class.
    private static readonly Lazy<Dictionary<UpdateSituationObject.TypeOneofCase, ISituationObjectMerger>> Mergers =
        new(() => AllMergers.CreateAll().ToDictionary(m => m.HandledCase));

    internal static SituationObject? Materialize(UpdateSituationObject update)
    {
        return Mergers.Value.TryGetValue(update.TypeCase, out var merger) ? merger.Merge(null, update) : null;
    }
}
