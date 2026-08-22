using System.Net;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Tool.Conformance;

// Verifies that whatever answers at --address behaves like the TacticalAPI
// Situation contract requires. Exit code 0 when every check that ran passed,
// 1 when any failed, 2 when the arguments were wrong.
var options = CommandLineOptions.Parse(args);
if (options is null)
{
    await Console.Error.WriteLineAsync(CommandLineOptions.Usage).ConfigureAwait(false);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(CommandLineOptions.Usage);
    return 0;
}

// The contract is plain h2c; Grpc.Net.Client refuses such an address without this.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

using var channel = options.GrpcWeb
    ? GrpcChannel.ForAddress(options.Address, new GrpcChannelOptions
    {
        HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()),

        // Pinning 1.1 is not optional here: GrpcChannel defaults every request to
        // HTTP/2, and a gRPC-Web endpoint listening on HTTP/1.1 answers that with
        // HTTP_1_1_REQUIRED - which surfaces as every single check failing on a
        // transport error rather than as anything to do with the contract.
        HttpVersion = HttpVersion.Version11,
        HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact
    })
    : GrpcChannel.ForAddress(options.Address);

var context = new ConformanceContext(
    new Situation.SituationClient(channel),
    options.ReporterId,
    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture),
    options.StreamTimeout);

if (!options.Json)
{
    // This suite writes to the situation it is checking. Saying so up front is
    // cheaper than explaining it afterwards to whoever was watching that situation.
    Console.WriteLine($"Running {SituationContractChecks.All.Count} check(s) against {options.Address}.");
    Console.WriteLine($"This adds and deletes objects under the identity prefix 'conformance:{context.RunId}:'.");
    Console.WriteLine();
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var reports = await ConformanceRunner
    .RunAsync(context, SituationContractChecks.All, options.IncludeSlow, cancellation.Token)
    .ConfigureAwait(false);

Console.WriteLine(options.Json
    ? ConformanceReportFormatter.FormatJson(reports, options.Address.ToString())
    : ConformanceReportFormatter.FormatText(reports, options.Address.ToString()));

return ConformanceReportFormatter.Count(reports, CheckOutcome.Failed) > 0 ? 1 : 0;
