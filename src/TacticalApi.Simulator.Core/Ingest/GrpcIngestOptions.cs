using System.ComponentModel.DataAnnotations;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     Configures the gRPC endpoint that simulation sources push updates to via
///     <see cref="GrpcSituationIngest" />. Defaults to the simulator's own
///     native gRPC endpoint, so sources keep working out of the box, but
///     pointing this at a different address lets the same sources drive any
///     other implementation of the TacticalAPI contract instead.
/// </summary>
public sealed class GrpcIngestOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = "Simulator:Ingest";

    /// <summary>Address of the TacticalAPI gRPC endpoint sources write to.</summary>
    [Required]
    public Uri Address { get; set; } = new("http://localhost:5100");
}
