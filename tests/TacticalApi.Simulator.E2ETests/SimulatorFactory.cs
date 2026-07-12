using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
/// Boots the REAL simulator host (Program.cs, full DI, gRPC pipeline) on an
/// in-memory TestServer. Simulation sources are disabled by default so tests
/// are deterministic; individual fixtures re-enable them via settings.
/// </summary>
public class SimulatorFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    /// <summary>Used by xunit as IClassFixture (requires a parameterless ctor).</summary>
    public SimulatorFactory()
        : this(null)
    {
    }

    /// <summary>Used by tests that need specific configuration overrides.</summary>
    internal SimulatorFactory(Dictionary<string, string?>? settings = null)
        => _settings = settings ?? new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("E2ETest");

        // Deterministic baseline: no background sources unless a test opts in.
        builder.UseSetting("Simulator:Sources:SyntheticScenario:Enabled", "false");
        builder.UseSetting("Simulator:Sources:SyntheticAirTracks:Enabled", "false");
        builder.UseSetting("Simulator:Sources:OpenSky:Enabled", "false");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>Native gRPC client (HTTP/2) against the in-memory server.</summary>
    public Situation.SituationClient CreateGrpcClient()
    {
        var channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = Server.CreateHandler(),
        });
        return new Situation.SituationClient(channel);
    }

    /// <summary>
    /// gRPC-Web client (HTTP/1.1) - the transport the official Rheinmetall
    /// test client uses.
    /// </summary>
    public Situation.SituationClient CreateGrpcWebClient()
    {
        var channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, Server.CreateHandler()),
        });
        return new Situation.SituationClient(channel);
    }
}
