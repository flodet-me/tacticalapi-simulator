using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>Renders a run's results, either for a person or for a pipeline.</summary>
public static class ConformanceReportFormatter
{
    /// <summary>Renders a plain-text report suitable for a terminal or a CI log.</summary>
    public static string FormatText(IReadOnlyList<CheckReport> reports, string address)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var builder = new StringBuilder();
        builder.Append("TacticalAPI conformance report for ").AppendLine(address);
        builder.AppendLine();

        foreach (var report in reports)
        {
            builder.Append(Marker(report.Result.Outcome)).Append(' ')
                .Append(report.Check.Id.PadRight(34))
                .Append(report.Check.Title);

            if (report.Duration > TimeSpan.Zero)
                builder.Append(CultureInfo.InvariantCulture, $" ({report.Duration.TotalMilliseconds:F0}ms)");

            builder.AppendLine();

            if (report.Result.Detail is { } detail)
                builder.Append("     ").AppendLine(detail);

            // The rule itself is only worth the space when it was broken - that is the
            // moment someone needs to see what the contract actually says.
            if (report.Result.Outcome == CheckOutcome.Failed)
                builder.Append("     contract: ").AppendLine(report.Check.Requirement);
        }

        var passed = Count(reports, CheckOutcome.Passed);
        var failed = Count(reports, CheckOutcome.Failed);
        var skipped = Count(reports, CheckOutcome.Skipped);

        builder.AppendLine();
        builder.Append(CultureInfo.InvariantCulture, $"{passed} passed, {failed} failed, {skipped} skipped");
        builder.AppendLine();

        return builder.ToString();
    }

    /// <summary>Renders the same results as JSON, for a pipeline to store or diff.</summary>
    public static string FormatJson(IReadOnlyList<CheckReport> reports, string address)
    {
        ArgumentNullException.ThrowIfNull(reports);

        return JsonSerializer.Serialize(new
        {
            address,
            generatedAt = DateTimeOffset.UtcNow,
            summary = new
            {
                passed = Count(reports, CheckOutcome.Passed),
                failed = Count(reports, CheckOutcome.Failed),
                skipped = Count(reports, CheckOutcome.Skipped)
            },
            checks = reports.Select(report => new
            {
                id = report.Check.Id,
                title = report.Check.Title,
                requirement = report.Check.Requirement,
                outcome = report.Result.Outcome.ToString().ToLowerInvariant(),
                detail = report.Result.Detail,
                durationMs = report.Duration.TotalMilliseconds
            })
        }, JsonOptions);
    }

    /// <summary>Number of reports with the given outcome.</summary>
    public static int Count(IReadOnlyList<CheckReport> reports, CheckOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var count = 0;
        foreach (var report in reports)
            if (report.Result.Outcome == outcome)
                count++;

        return count;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string Marker(CheckOutcome outcome)
    {
        return outcome switch
        {
            CheckOutcome.Passed => "PASS",
            CheckOutcome.Failed => "FAIL",
            _ => "SKIP"
        };
    }
}
