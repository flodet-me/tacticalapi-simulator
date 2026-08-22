using System.Diagnostics;
using Grpc.Core;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>Which subset of the suite to run, and how patient to be with it.</summary>
/// <param name="IncludeSlow">Run checks that take seconds (expiry sweeping).</param>
/// <param name="ReadOnly">Run only checks that never write to the situation.</param>
/// <param name="Only">If non-empty, run only these check ids.</param>
/// <param name="Skip">Check ids to leave out.</param>
public sealed record RunSelection(
    bool IncludeSlow = false,
    bool ReadOnly = false,
    IReadOnlySet<string>? Only = null,
    IReadOnlySet<string>? Skip = null)
{
    /// <summary>Whether <paramref name="check" /> runs, and if not, why it was skipped.</summary>
    public string? SkipReason(ConformanceCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (Only is { Count: > 0 } && !Only.Contains(check.Id)) return "not selected by --only";
        if (Skip is { Count: > 0 } && Skip.Contains(check.Id)) return "excluded by --skip";
        if (ReadOnly && check.Mutating) return "writes to the situation; excluded by --read-only";
        if (check.Slow && !IncludeSlow) return "slow check; pass --include-slow to run it";

        return null;
    }
}

/// <summary>Runs a set of checks against one implementation and collects the results.</summary>
public static class ConformanceRunner
{
    /// <summary>
    ///     Runs the selected checks. A check that throws is reported as a failure
    ///     carrying the exception rather than being allowed to abort the run: an
    ///     implementation that breaks one rule usually keeps the others, and a report
    ///     of everything is far more useful than a stack trace from the first thing
    ///     that went wrong.
    /// </summary>
    public static async Task<IReadOnlyList<CheckReport>> RunAsync(
        ConformanceContext context,
        IReadOnlyList<ConformanceCheck> checks,
        RunSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(selection);

        var reports = new List<CheckReport>(checks.Count);

        foreach (var check in checks)
        {
            if (selection.SkipReason(check) is { } reason)
            {
                reports.Add(new CheckReport(check, CheckResult.Skip(reason), TimeSpan.Zero));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            CheckResult result;
            try
            {
                result = await check.RunAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                result = CheckResult.Fail($"the call failed: {ex.StatusCode} {ex.Status.Detail}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result = CheckResult.Fail("the check timed out");
            }

            reports.Add(new CheckReport(check, result, stopwatch.Elapsed));
        }

        return reports;
    }

    /// <summary>
    ///     Whether the run should be treated as a failure.
    ///     Only <see cref="CheckSeverity.Required" /> failures count unless
    ///     <paramref name="strict" /> is set: an advisory failure means the
    ///     implementation differs from this simulator on something the contract does
    ///     not address, which is a conversation, not a verdict.
    /// </summary>
    public static bool HasFailed(IReadOnlyList<CheckReport> reports, bool strict)
    {
        ArgumentNullException.ThrowIfNull(reports);

        return reports.Any(report =>
            report.Result.Outcome == CheckOutcome.Failed
            && (strict || report.Check.Severity == CheckSeverity.Required));
    }
}

/// <summary>One check plus how it went and how long it took.</summary>
/// <param name="Check">The check that was run.</param>
/// <param name="Result">Its outcome.</param>
/// <param name="Duration">Wall-clock time the check took.</param>
public sealed record CheckReport(ConformanceCheck Check, CheckResult Result, TimeSpan Duration);
