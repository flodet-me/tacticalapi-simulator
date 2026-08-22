using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace TacticalApi.Simulator.Tool.Conformance;

/// <summary>Renders a run's results, for a person, a pipeline, or a CI test-report viewer.</summary>
public static class ConformanceReportFormatter
{
    /// <summary>Prefix of the generated per-object-type check ids.</summary>
    public const string ObjectTypePrefix = "object-type-";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Renders a plain-text report suitable for a terminal or a CI log.</summary>
    public static string FormatText(IReadOnlyList<CheckReport> reports, string address)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var builder = new StringBuilder();
        builder.Append("TacticalAPI conformance report for ").AppendLine(address);
        builder.AppendLine();

        // Width from the actual ids rather than a guessed constant: a check added
        // later with a longer id would otherwise run its title into the column.
        var idWidth = reports.Count == 0 ? 0 : reports.Max(r => r.Check.Id.Length) + 2;

        foreach (var report in reports)
        {
            builder.Append(Marker(report)).Append(' ')
                .Append(report.Check.Id.PadRight(idWidth))
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
        var advisory = reports.Count(r =>
            r.Result.Outcome == CheckOutcome.Failed && r.Check.Severity == CheckSeverity.Advisory);

        // Which of the eleven object types the implementation actually takes is the
        // single most useful line in this report for someone planning an integration,
        // and it would otherwise be spread across eleven rows in the middle.
        var (accepted, rejected) = ObjectTypeSupport(reports);
        if (accepted.Count + rejected.Count > 0)
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture,
                $"Object types accepted: {accepted.Count} of {accepted.Count + rejected.Count}");
            builder.AppendLine();

            if (rejected.Count > 0)
                builder.Append("  not accepted: ").AppendLine(string.Join(", ", rejected));
        }

        builder.AppendLine();
        builder.Append(CultureInfo.InvariantCulture, $"{passed} passed, {failed} failed, {skipped} skipped");

        // Spelling out the split matters: a run that "failed" only on advisory checks
        // exits 0, and a reader seeing FAIL lines above deserves to know why.
        if (advisory > 0)
            builder.Append(CultureInfo.InvariantCulture,
                $" ({advisory} of the failures advisory - the contract does not settle those)");

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
                skipped = Count(reports, CheckOutcome.Skipped),
                requiredFailures = reports.Count(r =>
                    r.Result.Outcome == CheckOutcome.Failed && r.Check.Severity == CheckSeverity.Required),
                advisoryFailures = reports.Count(r =>
                    r.Result.Outcome == CheckOutcome.Failed && r.Check.Severity == CheckSeverity.Advisory)
            },
            objectTypes = new
            {
                accepted = ObjectTypeSupport(reports).Accepted,
                notAccepted = ObjectTypeSupport(reports).Rejected
            },
            checks = reports.Select(report => new
            {
                id = report.Check.Id,
                title = report.Check.Title,
                requirement = report.Check.Requirement,
                severity = report.Check.Severity.ToString().ToLowerInvariant(),
                mutating = report.Check.Mutating,
                outcome = report.Result.Outcome.ToString().ToLowerInvariant(),
                detail = report.Result.Detail,
                durationMs = report.Duration.TotalMilliseconds
            })
        }, JsonOptions);
    }

    /// <summary>
    ///     Renders the results as JUnit XML, the format every CI already knows how to
    ///     display as a test report. A conformance run is a set of pass/fail
    ///     assertions, so there is no reason for it to show up in a pipeline as a wall
    ///     of log text nobody reads.
    ///     Advisory failures are emitted as skipped-with-a-message rather than as
    ///     failures, so a CI gate reflects the same verdict the exit code does.
    /// </summary>
    public static string FormatJUnit(IReadOnlyList<CheckReport> reports, string address, bool strict)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var counted = reports
            .Select(report => (Report: report, Failed: IsGatingFailure(report, strict)))
            .ToList();

        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false };
        var buffer = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartElement("testsuites");
            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", "tacticalapi-conformance");
            writer.WriteAttributeString("hostname", address);
            writer.WriteAttributeString("tests", Invariant(counted.Count));
            writer.WriteAttributeString("failures", Invariant(counted.Count(c => c.Failed)));
            writer.WriteAttributeString("skipped", Invariant(counted.Count(c => !c.Failed
                && c.Report.Result.Outcome != CheckOutcome.Passed)));
            writer.WriteAttributeString("time", Invariant(reports.Sum(r => r.Duration.TotalSeconds)));

            foreach (var (report, failed) in counted)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("classname", "rheinmetall.tactical_api.v0.Situation");
                writer.WriteAttributeString("name", report.Check.Id);
                writer.WriteAttributeString("time", Invariant(report.Duration.TotalSeconds));

                if (failed)
                {
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message", report.Result.Detail ?? report.Check.Title);
                    writer.WriteString($"{report.Check.Title}\n\ncontract: {report.Check.Requirement}");
                    writer.WriteEndElement();
                }
                else if (report.Result.Outcome != CheckOutcome.Passed)
                {
                    writer.WriteStartElement("skipped");
                    writer.WriteAttributeString("message", report.Result.Detail ?? "skipped");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return buffer.ToString();
    }

    /// <summary>Lists every check without running any of them (<c>--list</c>).</summary>
    public static string FormatCatalog(IReadOnlyList<ConformanceCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{checks.Count} check(s):").AppendLine();
        builder.AppendLine();

        var idWidth = checks.Count == 0 ? 0 : checks.Max(check => check.Id.Length) + 2;

        foreach (var check in checks)
        {
            var tags = new List<string> { check.Severity.ToString().ToLowerInvariant() };
            if (!check.Mutating) tags.Add("read-only");
            if (check.Slow) tags.Add("slow");

            builder.Append("  ").Append(check.Id.PadRight(idWidth))
                .Append('[').Append(string.Join(", ", tags)).Append("] ")
                .AppendLine(check.Title);
        }

        return builder.ToString();
    }

    /// <summary>Number of reports with the given outcome.</summary>
    public static int Count(IReadOnlyList<CheckReport> reports, CheckOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(reports);

        return reports.Count(report => report.Result.Outcome == outcome);
    }

    /// <summary>
    ///     Splits the per-object-type results into the types the implementation took
    ///     and the types it refused. Skipped types count as neither - they were not
    ///     asked about.
    /// </summary>
    public static (IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected) ObjectTypeSupport(
        IReadOnlyList<CheckReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var accepted = new List<string>();
        var rejected = new List<string>();

        foreach (var report in reports)
        {
            if (!report.Check.Id.StartsWith(ObjectTypePrefix, StringComparison.Ordinal)) continue;

            var name = report.Check.Id[ObjectTypePrefix.Length..];
            switch (report.Result.Outcome)
            {
                case CheckOutcome.Passed:
                    accepted.Add(name);
                    break;
                case CheckOutcome.Failed:
                    rejected.Add(name);
                    break;
                default:
                    break;
            }
        }

        return (accepted, rejected);
    }

    private static bool IsGatingFailure(CheckReport report, bool strict)
    {
        return report.Result.Outcome == CheckOutcome.Failed
               && (strict || report.Check.Severity == CheckSeverity.Required);
    }

    private static string Marker(CheckReport report)
    {
        return report.Result.Outcome switch
        {
            CheckOutcome.Passed => "PASS",
            // A failing advisory check is not a verdict on the implementation, and
            // giving it the same four letters as a real failure would make the report
            // read worse than the situation is.
            CheckOutcome.Failed => report.Check.Severity == CheckSeverity.Required ? "FAIL" : "WARN",
            _ => "SKIP"
        };
    }

    private static string Invariant(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Invariant(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     A <see cref="StringWriter" /> that reports UTF-8.
    ///     <see cref="XmlWriter" /> takes the encoding for its declaration from the
    ///     writer it is given, and a plain StringWriter says UTF-16 - so the file would
    ///     announce an encoding it is not saved in, which some XML parsers refuse
    ///     outright and others silently mis-read.
    /// </summary>
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
