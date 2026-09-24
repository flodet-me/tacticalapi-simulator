using System.Net.Http.Json;
using System.Text.Json;
using Rheinmetall.TacticalApi.V0;
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
        // Arrange - short sweep intervals so the two slow checks (situation expiry and
        // the blue force keep-alive timeout) don't have to wait the production
        // defaults out. The keep-alive timeout stays comfortably longer than any
        // single check takes, so the fast blue force checks aren't racing the sweeper.
        await using var factory = new SimulatorFactory(new Dictionary<string, string?>
        {
            ["Simulator:ExpirySweepInterval"] = "00:00:01",
            ["Simulator:BlueForce:KeepAliveTimeout"] = "00:00:05",
            ["Simulator:BlueForce:SweepInterval"] = "00:00:01"
        });

        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        // Act
        var reports = await ConformanceRunner.RunAsync(
            context, TacticalApiContractChecks.All, new RunSelection(IncludeSlow: true));

        // Assert
        var failed = reports.Where(r => r.Result.Outcome == CheckOutcome.Failed).ToList();
        Assert.True(
            failed.Count == 0,
            "the simulator failed its own conformance suite:\n"
            + string.Join('\n', failed.Select(r => $"  {r.Check.Id}: {r.Result.Detail}")));

        Assert.Equal(TacticalApiContractChecks.All.Count, reports.Count);
        Assert.All(reports, r => Assert.Equal(CheckOutcome.Passed, r.Result.Outcome));
    }

    [Fact]
    public async Task EveryServiceOfTheContract_IsCovered()
    {
        // The suite grew from one service to three. This is what stops the other two
        // quietly dropping out of the default run again - which would be invisible,
        // because a suite that checks fewer things passes more easily.
        await using var factory = new SimulatorFactory();
        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        // Act
        var reports = await ConformanceRunner.RunAsync(
            context, TacticalApiContractChecks.All, new RunSelection());

        // Assert
        Assert.NotEmpty(SituationContractChecks.All);
        Assert.NotEmpty(BlueForceContractChecks.All);
        Assert.NotEmpty(OwnPoseContractChecks.All);
        Assert.Equal(
            SituationContractChecks.All.Count + BlueForceContractChecks.All.Count + OwnPoseContractChecks.All.Count,
            TacticalApiContractChecks.All.Count);

        Assert.Contains(reports, r => r.Check.Id.StartsWith("blue-force-", StringComparison.Ordinal));
        Assert.Contains(reports, r => r.Check.Id.StartsWith("own-pose-", StringComparison.Ordinal));
        Assert.DoesNotContain(reports, r => r.Result.Outcome == CheckOutcome.Failed);
    }

    [Fact]
    public async Task SlowChecks_AreSkippedUnlessAskedFor()
    {
        // Arrange
        await using var factory = new SimulatorFactory();
        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        // Act
        var reports = await ConformanceRunner.RunAsync(context, TacticalApiContractChecks.All, new RunSelection());

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
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}", TimeSpan.FromSeconds(1));

        // Act
        var reports = await ConformanceRunner.RunAsync(context, TacticalApiContractChecks.All, new RunSelection());

        // Assert
        Assert.Contains(reports, r => r.Result.Outcome == CheckOutcome.Failed);
        Assert.True(ConformanceRunner.HasFailed(reports, strict: false),
            "a Host rejecting every write must fail at least one REQUIRED check, not only advisory ones");
    }

    [Fact]
    public void EveryCheck_HasAUniqueIdAndQuotesTheRuleItEnforces()
    {
        // A failure has to point at the contract, not at this tool's opinion.
        var ids = TacticalApiContractChecks.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(TacticalApiContractChecks.All, check =>
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Title));
            Assert.False(string.IsNullOrWhiteSpace(check.Requirement));
        });
    }

    [Fact]
    public async Task EveryOneofInTheContract_HasAGeneratedCheckPerCase()
    {
        // Three oneofs in the contract are places an implementation can quietly
        // support a subset: the object type, the identity kind, and the location kind.
        // Every hand-written check in the suite uses symbol + string_identity + point,
        // so without these an implementation handling only those would pass everything.
        // Generated from the descriptors, so a case added upstream is covered
        // automatically rather than going unnoticed.
        await using var factory = new SimulatorFactory();
        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        var generated = TacticalApiContractChecks.All
            .Where(c => c.Id.StartsWith(ConformanceReportFormatter.ObjectTypePrefix, StringComparison.Ordinal)
                        || c.Id.StartsWith(ConformanceReportFormatter.IdentityKindPrefix, StringComparison.Ordinal)
                        || c.Id.StartsWith(ConformanceReportFormatter.LocationKindPrefix, StringComparison.Ordinal))
            .ToList();

        // Act
        var reports = await ConformanceRunner.RunAsync(
            context, generated, new RunSelection(Only: generated.Select(c => c.Id).ToHashSet()));

        // Assert - one check per case of each oneof, all passing against the simulator.
        Assert.Equal(SituationObjects.AllTypes.Count, CountWithPrefix(generated,
            ConformanceReportFormatter.ObjectTypePrefix));
        Assert.Equal(SituationObjects.IdentityKinds.Count, CountWithPrefix(generated,
            ConformanceReportFormatter.IdentityKindPrefix));
        Assert.Equal(SituationObjects.LocationKinds.Count, CountWithPrefix(generated,
            ConformanceReportFormatter.LocationKindPrefix));
        Assert.All(reports, r => Assert.Equal(CheckOutcome.Passed, r.Result.Outcome));

        // And the capability summary reflects it.
        Assert.Equal(11, ConformanceReportFormatter
            .Capability(reports, ConformanceReportFormatter.ObjectTypePrefix).Accepted.Count);
        Assert.Equal(9, ConformanceReportFormatter
            .Capability(reports, ConformanceReportFormatter.LocationKindPrefix).Accepted.Count);
        Assert.Equal(4, ConformanceReportFormatter
            .Capability(reports, ConformanceReportFormatter.IdentityKindPrefix).Accepted.Count);
    }

    private static int CountWithPrefix(IEnumerable<ConformanceCheck> checks, string prefix)
    {
        return checks.Count(c => c.Id.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryObjectTypeInTheContract_HasItsOwnCheck()
    {
        // Without these the whole suite could pass against an implementation that only
        // ever handles Symbol - the single most likely way for a real integration to
        // come apart. Generated from the descriptors, so a twelfth type added upstream
        // is covered automatically rather than quietly going unchecked.
        await using var factory = new SimulatorFactory();
        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}");

        var typeChecks = TacticalApiContractChecks.All
            .Where(c => c.Id.StartsWith("object-type-", StringComparison.Ordinal))
            .ToList();

        // Act
        var reports = await ConformanceRunner.RunAsync(
            context, typeChecks, new RunSelection(Only: typeChecks.Select(c => c.Id).ToHashSet()));

        // Assert - eleven types in the contract, eleven checks, all passing.
        Assert.Equal(11, typeChecks.Count);
        Assert.All(reports, r => Assert.Equal(CheckOutcome.Passed, r.Result.Outcome));
        Assert.Contains(typeChecks, c => c.Id == "object-type-overlay-document");
        Assert.Contains(typeChecks, c => c.Id == "object-type-symbol");
    }

    [Fact]
    public async Task ReadOnlyRun_WritesNothingToTheSituation()
    {
        // The promise the mode makes. If it were ever broken, someone would find out
        // by running this against a situation that mattered.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();
        var http = factory.CreateClient();

        await client.AddOrUpdateSituationObjectsAsync(new AddOrUpdateSituationObjectsRequest
        {
            SituationObjects = { E2E.Symbol("e2e:readonly:untouched", DateTimeOffset.UtcNow, "ALPHA") }
        });

        var context = new ConformanceContext(
            factory.CreateGrpcChannel(), "E2E-Conformance", $"e2e{Guid.NewGuid():N}", TimeSpan.FromSeconds(2));

        // Act
        var reports = await ConformanceRunner.RunAsync(
            context, TacticalApiContractChecks.All, new RunSelection(ReadOnly: true));

        // Assert - the read-only checks ran and passed...
        var ran = reports.Where(r => r.Result.Outcome != CheckOutcome.Skipped).ToList();
        Assert.NotEmpty(ran);
        Assert.All(ran, r => Assert.Equal(CheckOutcome.Passed, r.Result.Outcome));
        Assert.All(ran, r => Assert.False(r.Check.Mutating));

        // ...and the situation is exactly as it was.
        var after = (await client.GetSituationObjectsAsync(new GetSituationObjectsRequest())).SituationObjects;
        Assert.Single(after);
        Assert.Equal("e2e:readonly:untouched", after[0].Symbol.Identity.StringIdentity);

        var state = await http.GetFromJsonAsync<JsonElement>("/api/control/state");
        Assert.Equal(1, state.GetProperty("situationObjects").GetInt32());
    }

    [Fact]
    public async Task Suite_LeavesNothingBehindAfterAFullRun()
    {
        // Every check cleans up after itself, so a run against a live situation is
        // safe to repeat. The identity prefix is the safety net, not the plan - this
        // asserts the plan actually works.
        await using var factory = new SimulatorFactory();
        var client = factory.CreateGrpcClient();

        var runId = $"e2e{Guid.NewGuid():N}";
        var context = new ConformanceContext(factory.CreateGrpcChannel(), "E2E-Conformance", runId);

        // Act
        await ConformanceRunner.RunAsync(context, TacticalApiContractChecks.All, new RunSelection());

        // Assert
        var remaining = (await client.GetSituationObjectsAsync(new GetSituationObjectsRequest()))
            .SituationObjects
            .Where(o => (SituationObjects.IdentityOf(o)?.StringIdentity ?? string.Empty)
                .Contains(runId, StringComparison.Ordinal))
            .Select(o => SituationObjects.IdentityOf(o)?.StringIdentity)
            .ToList();

        Assert.True(remaining.Count == 0,
            "the suite left objects behind: " + string.Join(", ", remaining));
    }
}
