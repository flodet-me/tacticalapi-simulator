using TacticalApi.Simulator.Tool.Conformance;
using Xunit;

namespace TacticalApi.Simulator.E2ETests;

/// <summary>
///     Runs the conformance suite against this repo's own Host.
///     Two things at once: it proves the simulator implements the contract it
///     claims to, and it proves the suite is capable of saying so - a checker that
///     fails its own reference implementation is worthless when pointed at someone
///     else's, and a checker that passes everything is worse.
///     (src/tools/TacticalApi.Simulator.Tool.Conformance/)
/// </summary>
public sealed class ConformanceSuiteE2ETests
{
    [Fact]
    public async Task EveryCheck_PassesAgainstTheSimulator()
    {
        // Arrange - a short sweep interval so the expiry check doesn't have to wait
        // the production default out.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:ExpirySweepInterval"] = "00:00:01"
        });

        var context = new ConformanceContext(
            factory.CreateGrpcClient(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        // Act
        var reports = await ConformanceRunner.RunAsync(context, SituationContractChecks.All, includeSlow: true);

        // Assert
        var failed = reports.Where(r => r.Result.Outcome == CheckOutcome.Failed).ToList();
        Assert.True(
            failed.Count == 0,
            "the simulator failed its own conformance suite:\n"
            + string.Join('\n', failed.Select(r => $"  {r.Check.Id}: {r.Result.Detail}")));

        Assert.Equal(SituationContractChecks.All.Count, reports.Count);
        Assert.All(reports, r => Assert.Equal(CheckOutcome.Passed, r.Result.Outcome));
    }

    [Fact]
    public async Task SlowChecks_AreSkippedUnlessAskedFor()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var context = new ConformanceContext(
            factory.CreateGrpcClient(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        // Act
        var reports = await ConformanceRunner.RunAsync(context, SituationContractChecks.All, includeSlow: false);

        // Assert
        Assert.Contains(reports, r => r.Result.Outcome == CheckOutcome.Skipped);
        Assert.DoesNotContain(reports, r => r.Result.Outcome == CheckOutcome.Failed);
    }

    [Fact]
    public async Task Checks_FailAgainstAnImplementationThatBreaksTheContract()
    {
        // Arrange - a Host told to reject every write is a stand-in for a broken
        // implementation. If the suite still passed here it would be proving nothing.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:Faults:Enabled"] = "true",
            ["Simulator:Faults:ErrorHeaderProbability"] = "1.0"
        });

        // A short stream timeout keeps this fast: against an implementation that
        // rejects everything, every streaming check costs exactly one timeout.
        var context = new ConformanceContext(
            factory.CreateGrpcClient(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}", TimeSpan.FromSeconds(1));

        // Act
        var reports = await ConformanceRunner.RunAsync(context, SituationContractChecks.All, includeSlow: false);

        // Assert
        Assert.Contains(reports, r => r.Result.Outcome == CheckOutcome.Failed);
    }

    [Fact]
    public void EveryCheck_HasAUniqueIdAndQuotesTheRuleItEnforces()
    {
        // A failure has to point at the contract, not at this tool's opinion.
        var ids = SituationContractChecks.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(SituationContractChecks.All, check =>
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Title));
            Assert.False(string.IsNullOrWhiteSpace(check.Requirement));
        });
    }
}
