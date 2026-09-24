using Microsoft.Extensions.Options;
using TacticalApi.Simulator.Core;
using TacticalApi.Simulator.Core.Configuration;
using TacticalApi.Simulator.Core.Control;
using TacticalApi.Simulator.Core.Events;
using TacticalApi.Simulator.Core.Logging;
using TacticalApi.Simulator.Core.Store;
using TacticalApi.Simulator.Host.Control;
using TacticalApi.Simulator.Host.Diagnostics;
using TacticalApi.Simulator.Host.Faults;
using TacticalApi.Simulator.Host.Services;
using TacticalApi.Simulator.Host.Web;

AppSettingsBootstrap.EnsureAppSettingsFile();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFileLogging(builder.Configuration);

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

builder.Services.AddOptions<ControlOptions>()
    .Bind(builder.Configuration.GetSection(ControlOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<FaultInjectionOptions>()
    .Bind(builder.Configuration.GetSection(FaultInjectionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<FaultInjector>();
builder.Services.AddSingleton<MetricsCollector>();

builder.Services.AddGrpc(options =>
{
    // Latency and RPC-level faults apply to every call, so they go on the pipeline
    // rather than into the service (see FaultInjectionInterceptor).
    options.Interceptors.Add<FaultInjectionInterceptor>();

    // No security features by request: no auth interceptors, no TLS below.
    var performance = builder.Configuration
        .GetSection(SimulatorOptions.SectionName)
        .Get<SimulatorOptions>()?.Performance ?? new PerformanceOptions();
    options.MaxReceiveMessageSize = performance.MaxReceiveMessageSizeMb * 1024 * 1024;
});
builder.Services.AddGrpcReflection();

builder.Services.AddSituationServer();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Built eagerly: the collector only sees measurements taken after its listener
// starts, so resolving it lazily on the first scrape would silently lose every
// count from before someone happened to look.
_ = app.Services.GetRequiredService<MetricsCollector>();

// gRPC-Web (HTTP/1.1) support so the official Rheinmetall test client - which
// uses GrpcWebHandler against http://localhost:4268 - works unmodified.
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

app.MapGrpcService<SituationGrpcService>().EnableGrpcWeb();

// The contract's other two services. Every service it defines is implemented, and
// nothing that isn't in it is - see ARCHITECTURE.md.
app.MapGrpcService<BlueForceTrackingGrpcService>().EnableGrpcWeb();
app.MapGrpcService<OwnPoseGrpcService>().EnableGrpcWeb();
app.MapGrpcReflectionService();
app.MapHealthChecks("/healthz");

// Scrapeable metrics for the knobs Simulator:Performance exposes - above all the
// subscriber-event drops that DropOldest would otherwise discard unobserved.
app.MapGet("/metrics", (MetricsCollector collector) =>
    Results.Text(PrometheusFormatter.Format(collector.Collect()), PrometheusFormatter.ContentType));

// Drive the simulator itself (pause/reset/inject). Not part of the Situation
// service by design - see ControlEndpoints.
app.MapControlEndpoints();

app.MapGet("/", (SituationStore store,
    SituationEventBroker broker,
    BlueForceStore blueForces,
    BlueForceEventBroker blueForceBroker,
    OwnPoseStore ownPose,
    PositionEventBroker positionBroker,
    TimeProvider timeProvider,
    SimulationPause pause,
    IOptionsMonitor<SimulatorOptions> options) => Results.Ok(new
    {
        service = "TacticalAPI Simulator",
        protos = new[]
        {
            "rheinmetall.tactical_api.v0.Situation",
            "rheinmetall.tactical_api.v0.BlueForceTracking",
            "rheinmetall.tactical_api.v0.OwnPose"
        },
        situationObjects = store.Count,
        subscribers = broker.SubscriberCount,
        blueForces = blueForces.Count,
        blueForceSubscribers = blueForceBroker.SubscriberCount,
        positionSources = ownPose.SourceCount,
        positionSubscribers = positionBroker.SubscriberCount,
        primaryPositionSource = ownPose.GetPosition(timeProvider.GetUtcNow())?.SourceIdentifier,
        reporterId = options.CurrentValue.ReporterId,
        paused = pause.IsPaused
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

// Blue forces are their own service, so they're their own map layer rather than
// being flattened in with situation objects: the GUI draws them differently
// (callsign, mount relationships, own-force highlight) and a client watching the
// map should be able to tell which service a track came from.
app.MapGet("/api/blueforces", (BlueForceStore store, IOptionsMonitor<MapUiOptions> options) =>
    options.CurrentValue.Enabled
        ? Results.Ok(BlueForceMapper.Map(store.GetSnapshot()))
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

await app.RunAsync();

/// <summary>Exposed for E2E tests via WebApplicationFactory.</summary>
// S1118: this is the standard ASP.NET Core marker-class pattern that lets
// WebApplicationFactory<Program> reference a top-level-statement Program as a
// generic type argument - it's never actually instantiated, only used via
// reflection, so a static/protected-ctor class wouldn't serve the same purpose.
#pragma warning disable S1118
public partial class Program;
#pragma warning restore S1118
