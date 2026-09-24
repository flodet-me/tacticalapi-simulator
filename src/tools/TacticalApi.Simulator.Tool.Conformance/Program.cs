using System.Globalization;
using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Rheinmetall.TacticalApi.V0;
using TacticalApi.Simulator.Tool.Conformance;

// Verifies that whatever answers at --address behaves like the TacticalAPI
// contract requires - all three of its services: Situation, BlueForceTracking
// and OwnPose. Exit codes: 0 conformant, 1 a required check
// failed, 2 bad arguments, 3 the endpoint could not be reached at all.
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

if (options.ListOnly)
{
    Console.WriteLine(ConformanceReportFormatter.FormatCatalog(TacticalApiContractChecks.All));
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

var client = new Situation.SituationClient(channel);
var context = new ConformanceContext(
    channel,
    options.ReporterId,
    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
    options.StreamTimeout);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

// Reachability is established before the suite runs so that "your server is down"
// and "your server is wrong" are different exit codes. A pipeline that treats a
// dead endpoint as a conformance failure will eventually get someone to "fix" a
// perfectly good implementation.
if (await Unreachable(client, cancellation.Token).ConfigureAwait(false) is { } error)
{
    await Console.Error.WriteLineAsync($"Could not reach {options.Address}: {error}").ConfigureAwait(false);
    return 3;
}

if (!options.Json)
{
    var selected = TacticalApiContractChecks.All.Count(check => options.Selection.SkipReason(check) is null);
    Console.WriteLine(
        $"Running {selected} of {TacticalApiContractChecks.All.Count} check(s) against {options.Address}.");

    // This suite writes to the situation it is checking unless told not to. Saying so
    // up front is cheaper than explaining it afterwards to whoever was watching it.
    if (options.Selection.ReadOnly)
    {
        Console.WriteLine("Read-only run: nothing will be written.");
    }
    else
    {
        Console.WriteLine($"This adds and deletes objects under the identity prefix 'conformance:{context.RunId}:'.");

        // Said out loud because it cannot be fixed by being more careful: neither
        // service has a delete. What these checks write stays until the implementation
        // times it out, and someone watching the map deserves to know that in advance.
        Console.WriteLine(
            "Blue forces and positions written by this run CANNOT be cleaned up - the contract gives "
            + "BlueForceTracking no delete RPC and OwnPose no way to un-report a position. They age out "
            + "on the implementation's own keep-alive timeout.");
    }

    Console.WriteLine();
}

var reports = await ConformanceRunner
    .RunAsync(context, TacticalApiContractChecks.All, options.Selection, cancellation.Token)
    .ConfigureAwait(false);

Console.WriteLine(options.Json
    ? ConformanceReportFormatter.FormatJson(reports, options.Address.ToString())
    : ConformanceReportFormatter.FormatText(reports, options.Address.ToString()));

if (options.JUnitPath is { } junitPath)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(junitPath));
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

    await File.WriteAllTextAsync(
            junitPath,
            ConformanceReportFormatter.FormatJUnit(reports, options.Address.ToString(), options.Strict),
            cancellation.Token)
        .ConfigureAwait(false);
}

return ConformanceRunner.HasFailed(reports, options.Strict) ? 1 : 0;

// A single unary call: enough to tell a dead or wrong-protocol endpoint from a
// live one, without asserting anything about the answer (that is the suite's job).
static async Task<string?> Unreachable(Situation.SituationClient client, CancellationToken cancellationToken)
{
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        await client
            .GetSituationObjectsAsync(new GetSituationObjectsRequest(), cancellationToken: timeout.Token)
            .ConfigureAwait(false);
        return null;
    }
    catch (RpcException ex)
    {
        return $"{ex.StatusCode} {ex.Status.Detail}";
    }
    catch (OperationCanceledException)
    {
        return "the call did not complete within 15s";
    }
}
