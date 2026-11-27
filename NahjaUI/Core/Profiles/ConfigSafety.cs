using System;
using System.IO;
using System.Linq;
using NahjaUI.Core.Layout;

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

    private const int UnlockCopiesToKeep = 5;

    public static string UnlockCopiesDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "backups", "before-unlock");

    /// <summary>A plain copy of the saved settings each time unlock mode starts (the last few are kept), in case a change is regretted after saving it.</summary>
    public static void BeforeUnlock(Plugin plugin)
    {
        try
        {
            plugin.SaveNow();

            var file = Plugin.PluginInterface.ConfigFile;
            if (!file.Exists)
                return;

            Directory.CreateDirectory(UnlockCopiesDirectory);
            file.CopyTo(Path.Combine(UnlockCopiesDirectory, $"NahjaUI-before-unlock-{DateTime.Now:yyyyMMdd-HHmmss}.json"), false);

            foreach (var old in new DirectoryInfo(UnlockCopiesDirectory).GetFiles("NahjaUI-before-unlock-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(UnlockCopiesToKeep))
                old.Delete();
        }
        catch (Exception ex)
        {
            // A failed copy must never stop unlock mode.
            Plugin.Log.Warning(ex, "Could not copy the NahjaUI settings before unlock mode.");
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

        MergeBardDotRows(config);
    }

    // The first version of the Bard DoT rows had two bars ("stormbite" and "causticbite"); they are now one row of two pips ("dots"). The saved
    // position of the first bar moves to the new row, and whatever was attached to one of the two old rows (the Repertoire row, the progress
    // bar and, through them, the trackers) is attached to the new row. Without this they hang from a row that no longer exists and disappear.
    // It does nothing once there is no old row left, so it can run every time.
    private static void MergeBardDotRows(Configuration config)
    {
        const string oldTop = "gauge.brd.stormbite";
        const string oldBottom = "gauge.brd.causticbite";
        const string merged = "gauge.brd.dots";

        if (config.Layouts.Remove(oldTop, out var top) && !config.Layouts.ContainsKey(merged))
            config.Layouts[merged] = top;

        config.Layouts.Remove(oldBottom);

        foreach (var table in new[] { config.JobLayouts, config.CommonLayouts })
        {
            if (table.Remove(oldTop, out var jobs) && !table.ContainsKey(merged))
                table[merged] = jobs;

            table.Remove(oldBottom);
        }

        foreach (var layout in config.Layouts.Values.Concat(config.JobLayouts.Values.SelectMany(jobs => jobs.Values)).Concat(config.CommonLayouts.Values.SelectMany(jobs => jobs.Values)))
        {
            if (layout.AttachTo is oldTop or oldBottom)
                layout.AttachTo = merged;
        }
    }
}
