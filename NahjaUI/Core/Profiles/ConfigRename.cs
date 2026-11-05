using System;
using System.IO;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// The game calls the casting bars "progress bars", so everything that was named "cast bar" (the settings of the bar named after the cast, the HP texts and the auras, now trackers) was renamed. Saved files still have the old
/// names (settings, saved positions, the type names inside the file), so they are converted by text once: in the configuration file before
/// it is read, and in the profiles that are imported. The game's own window names (CastBarEnemy, _CastBar) are never touched.
/// </summary>
internal static class ConfigRename
{
    private static readonly (string Old, string New)[] Pairs =
    {
        ("CastBars", "ProgressBars"), ("CastBar", "ProgressBar"), ("castBars", "progressBars"), ("castBar", "progressBar"), ("castbar", "progressbar"),
        ("Cast bars", "Progress bars"), ("Cast bar", "Progress bar"), ("cast bars", "progress bars"), ("cast bar", "progress bar"),
        ("Cast frame", "Progress bar"), ("cast frame", "progress bar"),
        // The settings of the bar that were named after the cast (the longer names go first).
        ("CastTimeMode", "ProgressBarTimeMode"), ("CastTime", "ProgressBarTime"), ("CastName", "ProgressBarName"), ("CastSide", "ProgressBarPosition"),
        ("ShowCast", "ShowProgressBar"), ("CastHeightPx", "ProgressBarHeightPx"), ("CastWidthPx", "ProgressBarWidthPx"), ("CastGapPx", "ProgressBarGapPx"),
        ("CastOffsetX", "ProgressBarOffsetX"), ("CastOffsetY", "ProgressBarOffsetY"), ("CastUninterruptibleColor", "ProgressBarUninterruptibleColor"),
        ("CastBackgroundColor", "ProgressBarBackgroundColor"), ("CastBorderColor", "ProgressBarBorderColor"),
        // The "auras" are "trackers" (the status rows of the frames were "PartyAura...": they are status rows).
        ("PartyAura", "StatusRow"), ("Auras", "Trackers"), ("Aura", "Tracker"), ("auras", "trackers"), ("aura", "tracker"),
        // The HP texts and their formats.
        ("\"Health\":", "\"Hp\":"), ("HealthPreset", "HpPreset"), ("HealthTextPreset", "HpTextPreset")
    };

    private const string KeepEnemy = "\u0001A\u0001";
    private const string KeepPlayer = "\u0001B\u0001";

    /// <summary>The text with the old names turned into the new ones (a text that has none comes back the same).</summary>
    public static string Apply(string text)
    {
        var result = text.Replace("CastBarEnemy", KeepEnemy).Replace("_CastBar", KeepPlayer);
        foreach (var (oldName, newName) in Pairs)
            result = result.Replace(oldName, newName);

        return result.Replace(KeepEnemy, "CastBarEnemy").Replace(KeepPlayer, "_CastBar");
    }

    /// <summary>Converts the saved configuration file. It must run before the configuration is read. A copy of the old file is kept next to it.</summary>
    public static void ConvertConfigFile()
    {
        try
        {
            var file = Plugin.PluginInterface.ConfigFile;
            if (!file.Exists)
                return;

            var text = File.ReadAllText(file.FullName);
            var converted = Apply(text);
            if (converted == text)
                return;

            File.WriteAllText(file.FullName + ".before-progress-bar", text);
            File.WriteAllText(file.FullName, converted);
        }
        catch (Exception ex)
        {
            // If it fails the plugin starts with the file as it is (the old names are simply not found and the defaults are used).
            Plugin.Log.Warning(ex, "Could not convert the saved settings to the new names.");
        }
    }
}
