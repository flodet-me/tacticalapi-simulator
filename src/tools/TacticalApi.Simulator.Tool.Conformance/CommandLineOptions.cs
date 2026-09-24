using System.Globalization;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>
///     Command-line arguments. Hand-parsed rather than pulled in from a package:
///     a dozen flags don't justify a dependency in a repo whose CI runs a licence
///     allow-list and a vulnerability scan over every package it restores.
/// </summary>
public sealed class CommandLineOptions
{
    // S1075 (no hardcoded URIs) is about configuration leaking into code; this is a
    // CLI default that exists precisely so it can be overridden with --address.
#pragma warning disable S1075
    /// <summary>
    ///     Default endpoint: the Host's own native gRPC address, so the common case
    ///     (check the simulator you just started) needs no arguments at all.
    /// </summary>
    public const string DefaultAddress = "http://localhost:5100";
#pragma warning restore S1075

    /// <summary>Usage text, printed for --help and for a bad argument.</summary>
    public const string Usage = """
        Usage: tacticalapi-conformance [options]

        Verifies that a TacticalAPI implementation behaves as the contract requires, across all
        three of its services: Situation, BlueForceTracking and OwnPose.

        By default it WRITES to the implementation under test: situation objects are added and
        deleted under a 'conformance:<run-id>:' identity prefix. Blue forces and positions it
        writes CANNOT be removed afterwards - the contract has no delete for either - and are
        left to age out on the implementation's own timeout. Use --read-only against a live
        system.

        Connection:
          --address <uri>      Endpoint to check (default: http://localhost:5100).
          --grpc-web           Use the gRPC-Web transport (HTTP/1.1) instead of native gRPC.
          --reporter <id>      Reporter identity to write as (default: TacticalAPI-Conformance).
          --stream-timeout <s> Seconds a streaming check waits before failing (default: 10).

        Selecting checks:
          --list               List every check and exit without running anything.
          --only <ids>         Run only these checks (comma-separated ids).
          --skip <ids>         Leave these checks out (comma-separated ids).
          --read-only          Run only the checks that never write to the situation.
          --include-slow       Also run checks that take seconds (expiry sweeping).

        Output:
          --json               Emit the report as JSON instead of text.
          --junit <path>       Also write a JUnit XML report to <path>.
          --strict             Treat advisory failures as failures too.
          -h, --help           Show this help.

        Exit codes: 0 conformant, 1 at least one required check failed, 2 bad arguments,
                    3 the endpoint could not be reached at all.
        """;

    /// <summary>Endpoint under test.</summary>
    public Uri Address { get; private init; } = new(DefaultAddress);

    /// <summary>Whether to talk gRPC-Web rather than native gRPC.</summary>
    public bool GrpcWeb { get; private init; }

    /// <summary>Reporter identity written onto every object this run creates.</summary>
    public string ReporterId { get; private init; } = "TacticalAPI-Conformance";

    /// <summary>How long a streaming check waits before reporting a failure.</summary>
    public TimeSpan StreamTimeout { get; private init; } = TimeSpan.FromSeconds(10);

    /// <summary>Which checks to run.</summary>
    public RunSelection Selection { get; private init; } = new();

    /// <summary>Whether advisory failures should fail the run too.</summary>
    public bool Strict { get; private init; }

    /// <summary>Whether to emit JSON instead of text.</summary>
    public bool Json { get; private init; }

    /// <summary>Path to also write a JUnit XML report to, or null.</summary>
    public string? JUnitPath { get; private init; }

    /// <summary>Whether to list the checks and exit.</summary>
    public bool ListOnly { get; private init; }

    /// <summary>Whether help was requested.</summary>
    public bool ShowHelp { get; private init; }

    /// <summary>Parses arguments, or returns null if they don't make sense.</summary>
    public static CommandLineOptions? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var address = new Uri(DefaultAddress);
        var reporter = "TacticalAPI-Conformance";
        var streamTimeout = TimeSpan.FromSeconds(10);
        var grpcWeb = false;
        var includeSlow = false;
        var readOnly = false;
        var json = false;
        var strict = false;
        var listOnly = false;
        string? junitPath = null;
        HashSet<string>? only = null;
        HashSet<string>? skip = null;

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
                case "--read-only":
                    readOnly = true;
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--list":
                    listOnly = true;
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
                case "--junit":
                    if (!TryTakeValue(args, ref index, out junitPath)) return null;
                    break;
                case "--only":
                    if (!TryTakeIds(args, ref index, out only)) return null;
                    break;
                case "--skip":
                    if (!TryTakeIds(args, ref index, out skip)) return null;
                    break;
                case "--stream-timeout":
                    if (!TryTakeValue(args, ref index, out var seconds)
                        || !double.TryParse(seconds, CultureInfo.InvariantCulture, out var parsedSeconds)
                        || parsedSeconds <= 0)
                        return null;
                    streamTimeout = TimeSpan.FromSeconds(parsedSeconds);
                    break;
                default:
                    return null;
            }
        }

        // An id that matches nothing is refused rather than silently running the whole
        // suite (or nothing at all) - a typo in --only should not look like a pass.
        if (UnknownIds(only) is { Count: > 0 } || UnknownIds(skip) is { Count: > 0 }) return null;

        return new CommandLineOptions
        {
            Address = address,
            GrpcWeb = grpcWeb,
            ReporterId = reporter,
            StreamTimeout = streamTimeout,
            Selection = new RunSelection(includeSlow, readOnly, only, skip),
            Strict = strict,
            Json = json,
            JUnitPath = junitPath,
            ListOnly = listOnly
        };
    }

    /// <summary>Ids in <paramref name="ids" /> that name no known check.</summary>
    public static IReadOnlyList<string> UnknownIds(IReadOnlySet<string>? ids)
    {
        if (ids is null) return [];

        var known = TacticalApiContractChecks.All.Select(check => check.Id).ToHashSet(StringComparer.Ordinal);
        return ids.Where(id => !known.Contains(id)).ToList();
    }

    private static bool TryTakeIds(string[] args, ref int index, out HashSet<string>? ids)
    {
        ids = null;
        if (!TryTakeValue(args, ref index, out var value)) return false;

        ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return ids.Count > 0;
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
