using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Rheinmetall.TacticalApi.V0;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     Boots the REAL simulator host (Program.cs, full DI, gRPC pipeline) on an
///     in-memory TestServer. The Host has no simulation sources of its own -
///     those live in the separate Adapter.* executables - so there's nothing
///     to disable here for determinism; only tests that spin up an adapter
///     alongside it (see <c>AdapterIntegrationE2ETests</c>) need
///     <paramref name="useRealServer" />.
/// </summary>
public class SimulatorFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly bool _useRealServer;
    private IHost? _realHost;

    /// <summary>Used by xunit as IClassFixture (requires a parameterless ctor).</summary>
    public SimulatorFactory()
        : this(null)
    {
    }

    /// <summary>
    ///     Used by tests that need specific configuration overrides. Pass
    ///     <paramref name="useRealServer" /> when a test also runs an adapter
    ///     against this Host: the adapter's gRPC client dials a real address
    ///     (default http://localhost:5100), and an in-memory TestServer has no
    ///     real address for that client to connect back to - only a real
    ///     Kestrel socket does.
    /// </summary>
    internal SimulatorFactory(Dictionary<string, string?>? settings, bool useRealServer = false)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        _useRealServer = useRealServer;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("E2ETest");

        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
    }

    /// <summary>
    ///     For <see cref="_useRealServer" />: builds a second, real-socket host
    ///     alongside the in-memory TestServer one, per the documented ASP.NET
    ///     Core "test against Kestrel" pattern. The TestServer host is still the
    ///     one returned/tracked by the base class (so disposal etc. behaves
    ///     normally); the Kestrel host just needs to be up so a source's own
    ///     gRPC client - running inside that same TestServer host's DI
    ///     container - has a real loopback address to dial.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (!_useRealServer) return base.CreateHost(builder);

        var testHost = builder.Build();

        builder.ConfigureWebHost(webHostBuilder => webHostBuilder.UseKestrel());
        _realHost = builder.Build();
        _realHost.Start();

        return testHost;
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_realHost is not null)
        {
            await _realHost.StopAsync().ConfigureAwait(false);
            _realHost.Dispose();
        }
    }

    /// <summary>Native gRPC client (HTTP/2), real sockets when <see cref="_useRealServer" />.</summary>
    public Situation.SituationClient CreateGrpcClient()
    {
        if (_useRealServer)
        {
            _ = Services; // forces WebApplicationFactory to build/start the host (incl. the real Kestrel one)
            EnableH2C();
            return new Situation.SituationClient(GrpcChannel.ForAddress("http://localhost:5100"));
        }

        var channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = Server.CreateHandler()
        });
        return new Situation.SituationClient(channel);
    }

    /// <summary>
    ///     gRPC-Web client (HTTP/1.1) - the transport the official Rheinmetall
    ///     test client uses.
    /// </summary>
    public Situation.SituationClient CreateGrpcWebClient()
    {
        var channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, Server.CreateHandler())
        });
        return new Situation.SituationClient(channel);
    }

    // Grpc.Net.Client requires this switch to call an h2c (HTTP/2 without TLS)
    // endpoint, matching the simulator's "no TLS" design (see ARCHITECTURE.md).
    private static void EnableH2C()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }
}
