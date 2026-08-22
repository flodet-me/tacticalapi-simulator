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
        "example-check", "An example check", "The contract says so.", false,
        (_, _) => Task.FromResult(CheckResult.Pass()));

    private static CheckReport Report(CheckOutcome outcome, string? detail = null)
    {
        return new CheckReport(Check, new CheckResult(outcome, detail), TimeSpan.FromMilliseconds(12));
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
        Assert.False(options.IncludeSlow);
        Assert.False(options.Json);
    }

    [Fact]
    public void Parse_ReadsEveryFlag()
    {
        var options = CommandLineOptions.Parse(
            ["--address", "http://other:1234", "--grpc-web", "--reporter", "ME", "--include-slow", "--json",
             "--stream-timeout", "2.5"]);

        Assert.NotNull(options);
        Assert.Equal(new Uri("http://other:1234"), options.Address);
        Assert.True(options.GrpcWeb);
        Assert.Equal("ME", options.ReporterId);
        Assert.True(options.IncludeSlow);
        Assert.True(options.Json);
        Assert.Equal(TimeSpan.FromSeconds(2.5), options.StreamTimeout);
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
    public void Parse_RejectsBadArguments(string pipeSeparatedArgs)
    {
        Assert.Null(CommandLineOptions.Parse(pipeSeparatedArgs.Split('|')));
    }

    [Fact]
    public async Task RunAsync_ReportsAThrowingCheckAsAFailureAndKeepsGoing()
    {
        // An implementation that breaks one rule usually keeps the others; a report of
        // everything beats a stack trace from the first thing that went wrong.
        var throwing = new ConformanceCheck("throws", "Throws", "n/a", false,
            (_, _) => throw new Grpc.Core.RpcException(
                new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gone")));

        var reports = await ConformanceRunner.RunAsync(
            new ConformanceContext(null!, "TEST", "run"), [throwing, Check], includeSlow: true);

        Assert.Equal(CheckOutcome.Failed, reports[0].Result.Outcome);
        Assert.Contains("Unavailable", reports[0].Result.Detail, StringComparison.Ordinal);
        Assert.Equal(CheckOutcome.Passed, reports[1].Result.Outcome);
    }

    [Fact]
    public async Task RunAsync_SkipsSlowChecksUnlessAskedFor()
    {
        var slow = new ConformanceCheck("slow", "Slow", "n/a", true,
            (_, _) => Task.FromResult(CheckResult.Pass()));

        var reports = await ConformanceRunner.RunAsync(
            new ConformanceContext(null!, "TEST", "run"), [slow], includeSlow: false);

        Assert.Equal(CheckOutcome.Skipped, Assert.Single(reports).Result.Outcome);
        Assert.Contains("--include-slow", reports[0].Result.Detail, StringComparison.Ordinal);
    }
}
