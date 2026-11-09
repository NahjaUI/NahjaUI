using System;
using System.IO;
using System.Linq;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// Keeps a plain copy of the saved settings file before the plugin changes version (or before the format has to be converted),
/// so an update can never leave someone without a way back.
/// </summary>
internal static class ConfigSafety
{
    private const int CopiesToKeep = 5;

    public static string CopiesDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "backups", "before-update");

    /// <summary>Call once at startup, before anything changes the settings.</summary>
    public static void BeforeUpdate(Configuration config)
    {
        try
        {
            var current = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0";
            var versionChanged = config.LastPluginVersion != current;
            var needsConversion = config.Version < Configuration.CurrentVersion;
            var fromNewer = config.Version > Configuration.CurrentVersion;

            var file = Plugin.PluginInterface.ConfigFile;
            if ((versionChanged || needsConversion || fromNewer) && file.Exists)
            {
                Directory.CreateDirectory(CopiesDirectory);

                var from = string.IsNullOrEmpty(config.LastPluginVersion) ? "unknown" : config.LastPluginVersion;
                var name = $"NahjaUI-v{from}-to-v{current}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                file.CopyTo(Path.Combine(CopiesDirectory, name), false);
                Trim();
            }

            config.LastPluginVersion = current;
        }
        catch (Exception ex)
        {
            // A failed copy must never stop the plugin from starting.
            Plugin.Log.Warning(ex, "Could not copy the NahjaUI settings before the update.");
        }
    }

    /// <summary>The copies made before updates, newest first.</summary>
    public static FileInfo[] List()
    {
        try
        {
            if (!Directory.Exists(CopiesDirectory))
                return Array.Empty<FileInfo>();

            return new DirectoryInfo(CopiesDirectory).GetFiles("NahjaUI-*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static void Trim()
    {
        foreach (var old in List().Skip(CopiesToKeep))
            old.Delete();
    }
}

/// <summary>
/// Converts settings saved by an older version of the plugin, one step per version number. There are no steps yet: the plugin has not
/// been published. When a public version changes the saved format, raise Configuration.CurrentVersion and add one case here.
/// </summary>
internal static class ConfigMigrations
{
    public static void Run(Configuration config)
    {
        while (config.Version < Configuration.CurrentVersion)
        {
            // One case per version number goes here, converting the settings of that version to the next one.
            config.Version++;
        }
    }
}
