using System.Reflection;

namespace TacticalApi.Simulator.Core;

/// <summary>
///     If "appsettings.json" doesn't already exist next to this process (e.g. a
///     bare published/copied deployment that dropped it), writes it out from
///     this executable's own embedded copy - the exact file shipped in source
///     control for this project - so every executable always has a
///     discoverable, editable config file on disk instead of silently running
///     on in-memory defaults with nothing to look at. Every
///     <c>TacticalApi.Simulator.*</c> executable project embeds its own
///     "appsettings.json" as a resource (<c>&lt;EmbeddedResource Include="appsettings.json"/&gt;</c>)
///     specifically so this works. Only the base file is handled -
///     "appsettings.{Environment}.json" is an optional override layer, not
///     something every deployment needs.
/// </summary>
public static class AppSettingsBootstrap
{
    private const string FileName = "appsettings.json";

    /// <summary>
    ///     Call once, before building the host/configuration, so a just-written
    ///     file is picked up by the same startup that wrote it.
    /// </summary>
    public static void EnsureAppSettingsFile()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        if (File.Exists(path)) return;

        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null) return;

        var resourceName = $"{assembly.GetName().Name}.{FileName}";
        using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource is null) return;

        try
        {
            using var file = File.Create(path);
            resource.CopyTo(file);
        }
        catch (IOException)
        {
            // Read-only deployment (e.g. some container filesystems) - the
            // process still runs fine on in-memory defaults, just without a
            // file to edit afterwards.
        }
        catch (UnauthorizedAccessException)
        {
            // See above.
        }
    }
}
