using System.Diagnostics;
using Grpc.Core;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>Runs a set of checks against one implementation and collects the results.</summary>
public static class ConformanceRunner
{
    /// <summary>
    ///     Runs every check in <paramref name="checks" />. A check that throws is
    ///     reported as a failure carrying the exception rather than being allowed to
    ///     abort the run: an implementation that breaks one rule usually keeps the
    ///     others, and a report of everything is far more useful than a stack trace
    ///     from the first thing that went wrong.
    /// </summary>
    public static async Task<IReadOnlyList<CheckReport>> RunAsync(
        ConformanceContext context,
        IReadOnlyList<ConformanceCheck> checks,
        bool includeSlow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checks);

        var reports = new List<CheckReport>(checks.Count);

        foreach (var check in checks)
        {
            if (check.Slow && !includeSlow)
            {
                reports.Add(new CheckReport(check, CheckResult.Skip("slow check; pass --include-slow to run it"),
                    TimeSpan.Zero));
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
}

/// <summary>One check plus how it went and how long it took.</summary>
/// <param name="Check">The check that was run.</param>
/// <param name="Result">Its outcome.</param>
/// <param name="Duration">Wall-clock time the check took.</param>
public sealed record CheckReport(ConformanceCheck Check, CheckResult Result, TimeSpan Duration);
