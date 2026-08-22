namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Command-line arguments. Hand-parsed rather than pulled in from a package: six
///     flags don't justify a dependency in a repo whose CI checks the licence and CVE
///     status of every one of them.
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>Usage text, printed for --help and for a bad argument.</summary>
    public const string Usage = """
        Usage: tacticalapi-conformance [options]

        Verifies that a TacticalAPI Situation implementation behaves as the contract requires.
        Writes to the situation under test: objects are added and deleted under a
        'conformance:<run-id>:' identity prefix.

        Options:
          --address <uri>     Endpoint to check (default: http://localhost:5100).
          --grpc-web          Use the gRPC-Web transport (HTTP/1.1) instead of native gRPC.
          --reporter <id>     Reporter identity to write as (default: TacticalAPI-Conformance).
          --include-slow      Also run checks that take seconds (expiry sweeping).
          --stream-timeout <s> Seconds a streaming check waits before failing (default: 10).
          --json              Emit the report as JSON instead of text.
          -h, --help          Show this help.

        Exit codes: 0 all checks passed, 1 at least one failed, 2 bad arguments.
        """;

    /// <summary>
    ///     Default endpoint: the Host's own native gRPC address, so the common case
    ///     (check the simulator you just started) needs no arguments at all.
    /// </summary>
    // S1075 (no hardcoded URIs) is about configuration leaking into code; this is a
    // CLI default that exists precisely so it can be overridden with --address.
#pragma warning disable S1075
    public const string DefaultAddress = "http://localhost:5100";
#pragma warning restore S1075

    /// <summary>Endpoint under test.</summary>
    public Uri Address { get; private init; } = new(DefaultAddress);

    /// <summary>Whether to talk gRPC-Web rather than native gRPC.</summary>
    public bool GrpcWeb { get; private init; }

    /// <summary>Reporter identity written onto every object this run creates.</summary>
    public string ReporterId { get; private init; } = "TacticalAPI-Conformance";

    /// <summary>Whether to run the checks marked slow.</summary>
    public bool IncludeSlow { get; private init; }

    /// <summary>How long a streaming check waits before reporting a failure.</summary>
    public TimeSpan StreamTimeout { get; private init; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether to emit JSON instead of text.</summary>
    public bool Json { get; private init; }

    /// <summary>Whether help was requested.</summary>
    public bool ShowHelp { get; private init; }

    /// <summary>Parses arguments, or returns null if they don't make sense.</summary>
    public static CommandLineOptions? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var address = new Uri(DefaultAddress);
        var reporter = "TacticalAPI-Conformance";
        var grpcWeb = false;
        var includeSlow = false;
        var json = false;
        var streamTimeout = TimeSpan.FromSeconds(10);

        var index = 0;
        while (index < args.Length)
        {
            var argument = args[index++];
            switch (argument)
            {
                case "-h" or "--help":
                    return new CommandLineOptions { ShowHelp = true };
                case "--grpc-web":
                    grpcWeb = true;
                    break;
                case "--include-slow":
                    includeSlow = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--address":
                    if (!TryTakeValue(args, ref index, out var value)
                        || !Uri.TryCreate(value, UriKind.Absolute, out var parsed))
                        return null;
                    address = parsed;
                    break;
                case "--reporter":
                    if (!TryTakeValue(args, ref index, out reporter)) return null;
                    break;
                case "--stream-timeout":
                    if (!TryTakeValue(args, ref index, out var seconds)
                        || !double.TryParse(seconds, System.Globalization.CultureInfo.InvariantCulture, out var parsedSeconds)
                        || parsedSeconds <= 0)
                        return null;
                    streamTimeout = TimeSpan.FromSeconds(parsedSeconds);
                    break;
                default:
                    return null;
            }
        }

        return new CommandLineOptions
        {
            Address = address,
            GrpcWeb = grpcWeb,
            ReporterId = reporter,
            IncludeSlow = includeSlow,
            Json = json,
            StreamTimeout = streamTimeout
        };
    }

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        if (index >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[index++];
        return true;
    }
}
