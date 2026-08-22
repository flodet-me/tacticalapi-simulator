using System.Globalization;
using System.Text;

namespace TacticalApi.Simulator.Host.Diagnostics;

/// <summary>
///     Renders collected series as Prometheus text exposition format (v0.0.4) - the
///     format every scraper, and <c>curl</c>, already understands.
/// </summary>
public static class PrometheusFormatter
{
    /// <summary>Content type Prometheus expects for this format.</summary>
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    /// <summary>Formats every series, emitting HELP/TYPE once per metric name.</summary>
    public static string Format(IReadOnlyList<MetricSeries> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var builder = new StringBuilder();
        string? lastName = null;

        foreach (var metric in series)
        {
            // HELP/TYPE belong to the metric, not the series, so they're written once
            // for a name however many label combinations follow - a scraper rejects
            // the whole payload if they repeat.
            if (metric.Name != lastName)
            {
                if (!string.IsNullOrEmpty(metric.Description))
                    builder.Append("# HELP ").Append(metric.Name).Append(' ').AppendLine(metric.Description);

                builder.Append("# TYPE ").Append(metric.Name).Append(' ')
                    .AppendLine(metric.IsCounter ? "counter" : "gauge");
                lastName = metric.Name;
            }

            builder.Append(metric.Name).Append(metric.Labels).Append(' ')
                .AppendLine(metric.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
