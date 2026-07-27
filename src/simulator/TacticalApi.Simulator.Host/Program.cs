using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Services;
using TacticalApi.Simulator.Host.Web;

AppSettingsBootstrap.EnsureAppSettingsFile();

var builder = WebApplication.CreateBuilder(args);

// Options via IOptionsMonitor: bound, validated at startup, hot-reloadable
// (appsettings.json changes are picked up without restarting the host).
builder.Services.AddOptions<SimulatorOptions>()
    .Bind(builder.Configuration.GetSection(SimulatorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<MapUiOptions>()
    .Bind(builder.Configuration.GetSection(MapUiOptions.SectionName))
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

builder.Services.AddSituationServer();

var app = builder.Build();

// gRPC-Web (HTTP/1.1) support so the official Rheinmetall test client - which
// uses GrpcWebHandler against http://localhost:4268 - works unmodified.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<SituationGrpcService>().EnableGrpcWeb();
app.MapGrpcReflectionService();
app.MapGet("/", (SituationStore store,
    SituationEventBroker broker,
    IOptionsMonitor<SimulatorOptions> options) => Results.Ok(new
    {
        service = "TacticalAPI Simulator",
        proto = "rheinmetall.tactical_api.v0.Situation",
        situationObjects = store.Count,
        subscribers = broker.SubscriberCount,
        reporterId = options.CurrentValue.ReporterId
    }));

// Read-only map GUI: static files under wwwroot/ui, backed by a JSON snapshot endpoint.
// Gated on MapUiOptions.Enabled per-request (not just at startup) so toggling it in
// appsettings.json hides the GUI without a restart, like every other option here.
app.UseWhen(
    ctx => ctx.RequestServices.GetRequiredService<IOptionsMonitor<MapUiOptions>>().CurrentValue.Enabled,
    branch => branch.UseStaticFiles());

app.MapGet("/ui", (IOptionsMonitor<MapUiOptions> options) =>
    options.CurrentValue.Enabled ? Results.Redirect("/ui/index.html") : Results.NotFound());
app.MapGet("/api/objects", (SituationStore store, IOptionsMonitor<MapUiOptions> options) =>
    options.CurrentValue.Enabled
        ? Results.Ok(SituationObjectMapper.Map(store.GetSnapshot()))
        : Results.NotFound());
app.MapGet("/api/config", (IOptionsMonitor<MapUiOptions> options) =>
{
    var o = options.CurrentValue;
    if (!o.Enabled) return Results.NotFound();

    return Results.Ok(new
    {
        refreshMs = o.RefreshInterval.TotalMilliseconds,
        defaultCenter = new { lat = o.DefaultCenterLatitude, lon = o.DefaultCenterLongitude },
        defaultZoom = o.DefaultZoom
    });
});

app.Run();

/// <summary>Exposed for E2E tests via WebApplicationFactory.</summary>
public partial class Program;
