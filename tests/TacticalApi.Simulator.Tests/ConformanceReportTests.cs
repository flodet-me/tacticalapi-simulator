using System.Text.Json;
using TacticalApi.Simulator.Tool.Conformance;
using Xunit;

namespace TacticalApi.Simulator.Tests;

/// <summary>
///     Unit tests for the conformance tool's reporting and argument parsing
///     (src/tools/TacticalApi.Simulator.Tool.Conformance/).
/// </summary>
public sealed class ConformanceReportTests
{
    private static readonly ConformanceCheck Check = new(
        "example-check", "An example check", "The contract says so.",
        CheckSeverity.Required, true, false,
        (_, _) => Task.FromResult(CheckResult.Pass()));

    private static readonly ConformanceCheck AdvisoryCheck = new(
        "example-advisory", "An advisory check", "The contract is silent on this.",
        CheckSeverity.Advisory, true, false,
        (_, _) => Task.FromResult(CheckResult.Pass()));

    private static CheckReport Report(CheckOutcome outcome, string? detail = null)
    {
        return new CheckReport(Check, new CheckResult(outcome, detail), TimeSpan.FromMilliseconds(12));
    }

    private static CheckReport AdvisoryReport(CheckOutcome outcome, string? detail = null)
    {
        return new CheckReport(AdvisoryCheck, new CheckResult(outcome, detail), TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public void FormatText_SummarizesEveryOutcome()
    {
        // Arrange
        List<CheckReport> reports =
            [Report(CheckOutcome.Passed), Report(CheckOutcome.Failed, "it did the wrong thing"),
             Report(CheckOutcome.Skipped, "not selected")];

        // Act
        var text = ConformanceReportFormatter.FormatText(reports, "http://localhost:5100");

        // Assert
        Assert.Contains("http://localhost:5100", text, StringComparison.Ordinal);
        Assert.Contains("1 passed, 1 failed, 1 skipped", text, StringComparison.Ordinal);
        Assert.Contains("PASS", text, StringComparison.Ordinal);
        Assert.Contains("FAIL", text, StringComparison.Ordinal);
        Assert.Contains("SKIP", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatText_QuotesTheContractOnlyForFailures()
    {
        // A failure is the moment someone needs to see what the contract actually
        // says; on a pass it is noise.
        var passing = ConformanceReportFormatter.FormatText([Report(CheckOutcome.Passed)], "addr");
        var failing = ConformanceReportFormatter.FormatText([Report(CheckOutcome.Failed, "nope")], "addr");

        Assert.DoesNotContain("contract:", passing, StringComparison.Ordinal);
        Assert.Contains("contract: The contract says so.", failing, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatJson_IsParseableAndCarriesTheSummary()
    {
        // Arrange
        List<CheckReport> reports = [Report(CheckOutcome.Passed), Report(CheckOutcome.Failed, "broken")];

        // Act
        var json = JsonDocument.Parse(ConformanceReportFormatter.FormatJson(reports, "http://host:5100"));

        // Assert
        var root = json.RootElement;
        Assert.Equal("http://host:5100", root.GetProperty("address").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("passed").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("failed").GetInt32());
        Assert.Equal("example-check", root.GetProperty("checks")[0].GetProperty("id").GetString());
        Assert.Equal("failed", root.GetProperty("checks")[1].GetProperty("outcome").GetString());
    }

    [Fact]
    public void Count_CountsOnlyTheGivenOutcome()
    {
        List<CheckReport> reports = [Report(CheckOutcome.Passed), Report(CheckOutcome.Passed),
            Report(CheckOutcome.Failed, "x")];

        Assert.Equal(2, ConformanceReportFormatter.Count(reports, CheckOutcome.Passed));
        Assert.Equal(1, ConformanceReportFormatter.Count(reports, CheckOutcome.Failed));
        Assert.Equal(0, ConformanceReportFormatter.Count(reports, CheckOutcome.Skipped));
    }

    [Fact]
    public void Parse_DefaultsToTheHostsOwnGrpcEndpoint()
    {
        // The common case - check the simulator you just started - takes no arguments.
        var options = CommandLineOptions.Parse([]);

        Assert.NotNull(options);
        Assert.Equal(new Uri(CommandLineOptions.DefaultAddress), options.Address);
        Assert.False(options.GrpcWeb);
        Assert.False(options.Selection.IncludeSlow);
        Assert.False(options.Selection.ReadOnly);
        Assert.False(options.Strict);
        Assert.False(options.Json);
    }

    [Fact]
    public void Parse_ReadsEveryFlag()
    {
        var options = CommandLineOptions.Parse(
            ["--address", "http://other:1234", "--grpc-web", "--reporter", "ME", "--include-slow", "--json",
             "--stream-timeout", "2.5", "--read-only", "--strict", "--junit", "out/report.xml",
             "--only", "get-reachable,add-get-roundtrip", "--skip", "add-get-roundtrip"]);

        Assert.NotNull(options);
        Assert.Equal(new Uri("http://other:1234"), options.Address);
        Assert.True(options.GrpcWeb);
        Assert.Equal("ME", options.ReporterId);
        Assert.True(options.Selection.IncludeSlow);
        Assert.True(options.Selection.ReadOnly);
        Assert.True(options.Strict);
        Assert.True(options.Json);
        Assert.Equal("out/report.xml", options.JUnitPath);
        Assert.Equal(TimeSpan.FromSeconds(2.5), options.StreamTimeout);
        Assert.Equal(["add-get-roundtrip", "get-reachable"], options.Selection.Only?.Order());
        Assert.Equal(["add-get-roundtrip"], options.Selection.Skip);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_RecognizesHelp(string flag)
    {
        var options = CommandLineOptions.Parse([flag]);

        Assert.NotNull(options);
        Assert.True(options.ShowHelp);
    }

    [Theory]
    // A flag whose value is missing, malformed, or nonsensical must be refused rather
    // than silently falling back to a default the caller didn't ask for.
    [InlineData("--address")]
    [InlineData("--address|not a uri")]
    [InlineData("--reporter")]
    [InlineData("--stream-timeout|0")]
    [InlineData("--stream-timeout|abc")]
    [InlineData("--nonsense")]
    // A --only/--skip id naming no known check is a typo, and a typo that silently
    // ran the whole suite (or nothing) would look exactly like a pass.
    [InlineData("--only|no-such-check")]
    [InlineData("--skip|no-such-check")]
    [InlineData("--only")]
    [InlineData("--junit")]
    public void Parse_RejectsBadArguments(string pipeSeparatedArgs)
    {
        Assert.Null(CommandLineOptions.Parse(pipeSeparatedArgs.Split('|')));
    }

    [Fact]
    public async Task RunAsync_ReportsAThrowingCheckAsAFailureAndKeepsGoing()
    {
        // An implementation that breaks one rule usually keeps the others; a report of
        // everything beats a stack trace from the first thing that went wrong.
        var throwing = new ConformanceCheck("throws", "Throws", "n/a",
            CheckSeverity.Required, true, false,
            (_, _) => throw new Grpc.Core.RpcException(
                new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gone")));

        var reports = await ConformanceRunner.RunAsync(
            new ConformanceContext(null!, "TEST", "run"), [throwing, Check], new RunSelection(IncludeSlow: true));

        Assert.Equal(CheckOutcome.Failed, reports[0].Result.Outcome);
        Assert.Contains("Unavailable", reports[0].Result.Detail, StringComparison.Ordinal);
        Assert.Equal(CheckOutcome.Passed, reports[1].Result.Outcome);
    }

    [Fact]
    public async Task RunAsync_SkipsSlowChecksUnlessAskedFor()
    {
        var slow = new ConformanceCheck("slow", "Slow", "n/a", CheckSeverity.Required, true, true,
            (_, _) => Task.FromResult(CheckResult.Pass()));

        var reports = await ConformanceRunner.RunAsync(
            new ConformanceContext(null!, "TEST", "run"), [slow], new RunSelection());

        Assert.Equal(CheckOutcome.Skipped, Assert.Single(reports).Result.Outcome);
        Assert.Contains("--include-slow", reports[0].Result.Detail, StringComparison.Ordinal);
    }

    // --- Severity ---------------------------------------------------------------------

    [Fact]
    public void HasFailed_IgnoresAdvisoryFailuresUnlessStrict()
    {
        // An advisory failure means the implementation differs from this simulator on
        // something the contract does not address. Failing a pipeline over that would
        // be the fastest way to get the tool switched off.
        List<CheckReport> reports = [Report(CheckOutcome.Passed), AdvisoryReport(CheckOutcome.Failed, "differs")];

        Assert.False(ConformanceRunner.HasFailed(reports, strict: false));
        Assert.True(ConformanceRunner.HasFailed(reports, strict: true));
    }

    [Fact]
    public void HasFailed_AlwaysFailsOnARequiredFailure()
    {
        List<CheckReport> reports = [Report(CheckOutcome.Failed, "broken")];

        Assert.True(ConformanceRunner.HasFailed(reports, strict: false));
        Assert.True(ConformanceRunner.HasFailed(reports, strict: true));
    }

    [Fact]
    public void FormatText_DistinguishesAdvisoryFailuresFromRealOnes()
    {
        // Same four letters for both would make the report read worse than the
        // situation actually is.
        var text = ConformanceReportFormatter.FormatText(
            [Report(CheckOutcome.Failed, "broken"), AdvisoryReport(CheckOutcome.Failed, "differs")], "addr");

        Assert.Contains("FAIL example-check", text, StringComparison.Ordinal);
        Assert.Contains("WARN example-advisory", text, StringComparison.Ordinal);
        Assert.Contains("1 of the failures advisory", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatJson_SplitsRequiredAndAdvisoryFailures()
    {
        var json = JsonDocument.Parse(ConformanceReportFormatter.FormatJson(
            [Report(CheckOutcome.Failed, "broken"), AdvisoryReport(CheckOutcome.Failed, "differs")], "addr"));

        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("requiredFailures").GetInt32());
        Assert.Equal(1, summary.GetProperty("advisoryFailures").GetInt32());
        Assert.Equal("advisory", json.RootElement.GetProperty("checks")[1].GetProperty("severity").GetString());
    }

    // --- Selection --------------------------------------------------------------------

    [Fact]
    public void Selection_RunsEverythingByDefaultExceptSlowChecks()
    {
        var selection = new RunSelection();

        Assert.Null(selection.SkipReason(Check));
        Assert.NotNull(selection.SkipReason(Check with { Slow = true }));
    }

    [Fact]
    public void Selection_ReadOnlyExcludesEveryMutatingCheck()
    {
        // The point of the mode: it has to be safe to point at a situation somebody
        // is relying on, so anything that writes is excluded by construction.
        var selection = new RunSelection(ReadOnly: true);

        Assert.NotNull(selection.SkipReason(Check));
        Assert.Null(selection.SkipReason(Check with { Mutating = false }));
    }

    [Fact]
    public void Selection_OnlyAndSkipNarrowTheRun()
    {
        var only = new RunSelection(Only: new HashSet<string> { "example-check" });
        Assert.Null(only.SkipReason(Check));
        Assert.NotNull(only.SkipReason(AdvisoryCheck));

        var skip = new RunSelection(Skip: new HashSet<string> { "example-check" });
        Assert.NotNull(skip.SkipReason(Check));
        Assert.Null(skip.SkipReason(AdvisoryCheck));
    }

    [Fact]
    public async Task RunAsync_ReportsSkippedChecksWithTheReason()
    {
        var reports = await ConformanceRunner.RunAsync(
            new ConformanceContext(null!, "TEST", "run"), [Check], new RunSelection(ReadOnly: true));

        var report = Assert.Single(reports);
        Assert.Equal(CheckOutcome.Skipped, report.Result.Outcome);
        Assert.Contains("--read-only", report.Result.Detail, StringComparison.Ordinal);
    }

    // --- Machine-readable output ------------------------------------------------------

    [Fact]
    public void FormatJUnit_IsWellFormedAndCountsGatingFailuresOnly()
    {
        // Advisory failures are emitted as skipped so a CI gate reflects the same
        // verdict the exit code does.
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(ConformanceReportFormatter.FormatJUnit(
            [Report(CheckOutcome.Passed), Report(CheckOutcome.Failed, "broken"),
             AdvisoryReport(CheckOutcome.Failed, "differs")],
            "http://host:5100", strict: false));

        var suite = xml.SelectSingleNode("/testsuites/testsuite")!;
        Assert.Equal("3", suite.Attributes!["tests"]!.Value);
        Assert.Equal("1", suite.Attributes["failures"]!.Value);
        Assert.Equal("1", suite.Attributes["skipped"]!.Value);
        Assert.Equal("http://host:5100", suite.Attributes["hostname"]!.Value);
        Assert.Equal(3, xml.SelectNodes("/testsuites/testsuite/testcase")!.Count);
    }

    [Fact]
    public void FormatJUnit_DeclaresTheEncodingItIsActuallyWrittenIn()
    {
        // XmlWriter takes the declaration from its TextWriter, and a plain
        // StringWriter claims UTF-16 - a file announcing an encoding it isn't saved in
        // is refused outright by some XML parsers.
        var xml = ConformanceReportFormatter.FormatJUnit([Report(CheckOutcome.Passed)], "addr", strict: false);

        Assert.Contains("encoding=\"utf-8\"", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatJUnit_CountsAdvisoryFailuresWhenStrict()
    {
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(ConformanceReportFormatter.FormatJUnit(
            [AdvisoryReport(CheckOutcome.Failed, "differs")], "addr", strict: true));

        Assert.Equal("1", xml.SelectSingleNode("/testsuites/testsuite")!.Attributes!["failures"]!.Value);
    }

    [Fact]
    public void FormatCatalog_ListsEveryCheckWithItsTags()
    {
        var catalog = ConformanceReportFormatter.FormatCatalog(SituationContractChecks.All);

        Assert.Contains($"{SituationContractChecks.All.Count} check(s)", catalog, StringComparison.Ordinal);
        Assert.Contains("get-reachable", catalog, StringComparison.Ordinal);
        Assert.Contains("read-only", catalog, StringComparison.Ordinal);
        Assert.Contains("advisory", catalog, StringComparison.Ordinal);
        Assert.Contains("slow", catalog, StringComparison.Ordinal);
    }
}
