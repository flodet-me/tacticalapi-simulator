namespace TacticalApi.Simulator.Core.Merging;

/// <summary>Single source of truth for the full merger set.</summary>
public static class AllMergers
{
    /// <summary>Creates one fresh instance of every registered merger.</summary>
    public static IReadOnlyList<ISituationObjectMerger> CreateAll()
    {
        return
        [
            new SymbolMerger(),
            new ActionTaskMerger(),
            new ActionEventMerger(),
            new OrganizationUnitMerger(),
            new RouteMerger(),
            new TextDocumentMerger(),
            new PictureDocumentMerger(),
            new VoiceMessageDocumentMerger(),
            new NatoMessageDocumentMerger(),
            new OverlayDocumentMerger(),
            new SketchDocumentMerger()
        ];
    }
}
