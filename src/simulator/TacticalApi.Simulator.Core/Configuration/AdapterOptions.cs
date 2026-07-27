namespace TacticalApi.Simulator.Core.Configuration;

/// <summary>
///     Marker for the configuration root every <c>Adapter.*</c> executable's own
///     settings live under - the ingest client (<see cref="TacticalApi.Simulator.Core.Ingest.GrpcIngestOptions" />)
///     and every source's own options. Deliberately separate from
///     <see cref="SimulatorOptions" />'s "Simulator" section: an adapter isn't the
///     simulator, it's a process that feeds one, so it gets its own config root
///     instead of borrowing the Host's.
/// </summary>
public static class AdapterOptions
{
    /// <summary>Configuration section name every adapter-owned option binds under.</summary>
    public const string SectionName = "Adapter";
}
