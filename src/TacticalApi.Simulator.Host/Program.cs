using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Host.Services;
using TacticalApi.Simulator.Sources;

var builder = WebApplication.CreateBuilder(args);

// Options via IOptionsMonitor: bound, validated at startup, hot-reloadable
// (appsettings.json changes are picked up without restarting the host).
builder.Services.AddOptions<SimulatorOptions>()
    .Bind(builder.Configuration.GetSection(SimulatorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddGrpc(options =>
{
    // No security features by request: no auth interceptors, no TLS below.
    var performance = builder.Configuration
        .GetSection(SimulatorOptions.SectionName)
        .Get<SimulatorOptions>()?.Performance ?? new PerformanceOptions();
    options.MaxReceiveMessageSize = performance.MaxReceiveMessageSizeMb * 1024 * 1024;
});
builder.Services.AddGrpcReflection();

builder.Services.AddSimulatorCore();
builder.Services.AddBundledSimulationSources(builder.Configuration);

var app = builder.Build();

// gRPC-Web (HTTP/1.1) support so the official Rheinmetall test client - which
// uses GrpcWebHandler against http://localhost:4268 - works unmodified.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<SituationGrpcService>().EnableGrpcWeb();
app.MapGrpcReflectionService();
app.MapGet("/", (TacticalApi.Simulator.Core.Store.SituationStore store,
                 TacticalApi.Simulator.Core.Events.SituationEventBroker broker,
                 IOptionsMonitor<SimulatorOptions> options) => Results.Ok(new
{
    service = "TacticalAPI Simulator",
    proto = "rheinmetall.tactical_api.v0.Situation",
    situationObjects = store.Count,
    subscribers = broker.SubscriberCount,
    reporterId = options.CurrentValue.ReporterId,
}));

app.Run();
