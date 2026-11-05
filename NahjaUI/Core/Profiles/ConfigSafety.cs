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
/// Converts settings saved by an older version of the plugin, one step per version number. Small additions that are safe to repeat
/// (the default filters, for example) use their own flags and do not need a step here.
/// </summary>
internal static class ConfigMigrations
{
    public static void Run(Configuration config)
    {
        while (config.Version < Configuration.CurrentVersion)
        {
            switch (config.Version)
            {
                case 1:
                    // 1 -> 2: the starting point of numbered versions. Everything before it was converted by flags inside the settings.
                    break;

                case 2:
                    // 2 -> 3: the stance trackers of the tanks and the Closed Position warning of the Dancer are shown only while the status
                    // is missing (they used to be always there, red when missing).
                    OnlyWhenMissing(config);
                    break;
            }

            config.Version++;
        }
    }

    private static readonly string[] WarningTrackers = { "Iron Will", "Defiance", "Grit", "Royal Guard", "Closed Position" };

    private static void OnlyWhenMissing(Configuration config)
    {
        foreach (var tracker in config.Trackers)
        {
            if (Array.IndexOf(WarningTrackers, tracker.Name) < 0 || tracker.Rules.Count != 1)
                continue;

            var rule = tracker.Rules[0];
            if (rule.Kind != NahjaUI.Modules.Trackers.ConditionKind.StatusAlways
                || rule.RedWhen != NahjaUI.Modules.Trackers.EffectWhen.StatusMissing
                || rule.DimWhen != NahjaUI.Modules.Trackers.EffectWhen.StatusMissing)
                continue;

            rule.Kind = NahjaUI.Modules.Trackers.ConditionKind.StatusMissing;
            rule.RedWhen = NahjaUI.Modules.Trackers.EffectWhen.Always;
            rule.DimWhen = NahjaUI.Modules.Trackers.EffectWhen.Always;
        }
    }
}
