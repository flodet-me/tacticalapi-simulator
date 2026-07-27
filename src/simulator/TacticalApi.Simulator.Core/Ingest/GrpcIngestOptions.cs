using System.ComponentModel.DataAnnotations;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Ingest;

/// <summary>
///     Configures the gRPC endpoint that simulation sources push updates to via
///     <see cref="GrpcSituationIngest" /> - any implementation of the TacticalAPI
///     contract, not necessarily this repo's own <c>Host</c>. Defaults to the
///     Host's native gRPC endpoint so sources keep working out of the box when
///     run alongside it, but pointing this at a different address drives any
///     other TacticalAPI implementation instead.
/// </summary>
public sealed class GrpcIngestOptions
{
    /// <summary>Configuration section name this options type binds to.</summary>
    public const string SectionName = SimulatorOptions.SectionName + ":Ingest";

    /// <summary>Address of the TacticalAPI gRPC endpoint sources write to.</summary>
    [Required]
    public Uri Address { get; set; } = new("http://localhost:5100");
}
