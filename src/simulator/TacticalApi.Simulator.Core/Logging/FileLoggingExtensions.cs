using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Json;
using TacticalApi.Simulator.Core.Configuration;

namespace TacticalApi.Simulator.Core.Logging;

/// <summary>Wires the optional rolling-file log sink into an executable's host builder.</summary>
public static class FileLoggingExtensions
{
    /// <summary>
    ///     Adds a daily rolling file sink alongside whatever providers are already
    ///     registered (console, etc.), when "Logging:File:Enabled" is true; a no-op
    ///     otherwise. Level filtering still comes from the standard "Logging:LogLevel"
    ///     section - Microsoft.Extensions.Logging applies that across every provider,
    ///     this one included, so it doesn't need its own copy of that config.
    /// </summary>
    /// <remarks>
    ///     Every log call in this codebase already goes through a source-generated
    ///     <c>[LoggerMessage]</c> method with a named-parameter template (see each
    ///     project's own <c>Logging/Log.cs</c>) - that's what makes a log call
    ///     "structured" in the first place: the arguments reach every provider as
    ///     distinct properties, not just text baked into one string. What a plain
    ///     text file sink would throw away is that structure on the way out - it'd
    ///     flatten every property back into one line. <see cref="JsonFormatter" />
    ///     keeps each property as its own field in the written JSON object instead
    ///     (timestamp, level, rendered message, message template, and every
    ///     {NamedProperty} from the call site), so the file stays queryable by field
    ///     - by a log aggregator or a one-off <c>jq</c> query alike - instead of only
    ///     grep-able by substring.
    /// </remarks>
    public static void AddFileLogging(this ILoggingBuilder logging, IConfiguration configuration)
    {
        var options = configuration.GetSection(FileLoggingOptions.SectionName).Get<FileLoggingOptions>()
                      ?? new FileLoggingOptions();
        if (!options.Enabled) return;

        var fileLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose() // defer level filtering to Microsoft.Extensions.Logging's own "LogLevel" config
            .WriteTo.File(new JsonFormatter(renderMessage: true), options.Path,
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31, shared: true)
            .CreateLogger();

        logging.AddSerilog(fileLogger, dispose: true);
    }
}
