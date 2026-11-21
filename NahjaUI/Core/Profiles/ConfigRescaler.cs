using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace NahjaUI.Core.Profiles;

/// <summary>
/// Makes every element of the interface bigger or smaller in one go, by changing the numbers saved in the settings: sizes of elements, icons and
/// letters, spaces and positions (offsets) are multiplied by a percentage, so the layout keeps its shape. It is done when the user asks:
/// nothing is scaled while drawing, and afterwards each element has plain sizes that can be edited as usual.
/// 100 % is always the ORIGINAL size: the percentage is absolute, not relative to what there is now. For that, the original value of every
/// number it changes is kept in a file ("resize-base.json" in the settings folder) together with the value it set, so going to any
/// percentage (and back to 100 %) is exact. A number that the user edited by hand after a resize is taken as the new original for itself.
/// A copy of the settings file is written before each resize. Counts (icons per line...), anchors, colors, rounding and the text size of the
/// menu are not touched.
/// </summary>
internal sealed class ConfigRescaler
{
    private const int CopiesToKeep = 5;
    public const int MinPercent = 50;
    public const int MaxPercent = 200;

    // Sizes, spaces and offsets by the name of the setting.
    private static readonly Regex SizeName = new(@"(Size|SizePx|Width|WidthPx|Height|HeightPx|Spacing|SpacingPx|Gap|GapPx|OffsetX|OffsetY)$", RegexOptions.Compiled);

    // Heights of letters: they end up on the nearest size of letter that exists.
    private static readonly Regex TextName = new(@"(TextSize|TimerSize|NameSize)$|^Size$", RegexOptions.Compiled);

    private static readonly Regex WidthName = new(@"Width(Px)?$", RegexOptions.Compiled);
    private static readonly Regex HeightName = new(@"Height(Px)?$", RegexOptions.Compiled);

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "MenuTextSize", "Rounding", "JobIconStyle", "IconsPerLine", "MaxIcons", "StreamerSeed", "LastPluginVersion", "Version", "ResizePercent"
    };

    // For each number that was changed: the original and the value that was set (to tell whether the user changed it after).
    public sealed class Entry
    {
        public double Original { get; set; }
        public double Set { get; set; }
    }

    private readonly Plugin plugin;
    private Dictionary<string, Entry>? entries;

    public ConfigRescaler(Plugin plugin)
    {
        this.plugin = plugin;
    }

    /// <summary>The percentage the interface has now (100 = the original size).</summary>
    public int AppliedPercent => plugin.Configuration.ResizePercent;

    /// <summary>A message to show under the controls (only when something went wrong).</summary>
    public string Message { get; set; } = string.Empty;

    // The copy of the settings file is made once for each time the slider is moved.
    private bool copied;

    public static string CopiesDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "backups", "before-resize");

    private static string BaseFile => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "resize-base.json");

    /// <summary>
    /// Takes the interface to <paramref name="percent"/> % of its original size, right now, without writing anything to disk (it is meant to be
    /// called while the slider moves, so the interface follows it on the screen). Call <see cref="Commit"/> when the slider is let go.
    /// </summary>
    public void Preview(int percent)
    {
        percent = Math.Clamp(percent, MinPercent, MaxPercent);
        var applied = Math.Clamp(AppliedPercent, MinPercent, MaxPercent);
        if (percent == applied)
            return;

        try
        {
            // The first change of a movement: a copy of the settings as they are (only a safety net: the original sizes are kept anyway).
            if (!copied)
            {
                copied = true;
                plugin.SaveNow();
                CopyFile();
            }

            var old = Load();
            var fresh = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var changed = 0;

            Walk(plugin.Configuration, string.Empty, percent / 100.0, applied, old, fresh, seen, ref changed);

            plugin.Configuration.ResizePercent = percent;

            // At 100 % every number is the original one: nothing needs to be remembered.
            entries = percent == 100 ? new Dictionary<string, Entry>() : fresh;
            Message = string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Resizing the NahjaUI interface failed.");
            Message = "The resize failed. Close and open the game again before trying once more.";
        }
    }

    /// <summary>Saves the result of the movement of the slider: the original sizes and the settings.</summary>
    public void Commit()
    {
        copied = false;
        Store();
        plugin.TrackerSystem.ReloadFromConfiguration();
        plugin.SaveNow();
    }

    /// <summary>Goes back to the original size (100 %).</summary>
    public void BackToNormal()
    {
        Preview(100);
        Commit();
    }

    private Dictionary<string, Entry> Load()
    {
        if (entries is not null)
            return entries;

        entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(BaseFile))
                entries = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(BaseFile)) ?? entries;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read the original sizes of the NahjaUI interface.");
        }

        return entries;
    }

    private void Store()
    {
        try
        {
            if (entries is null || entries.Count == 0)
            {
                if (File.Exists(BaseFile))
                    File.Delete(BaseFile);

                return;
            }

            File.WriteAllText(BaseFile, JsonConvert.SerializeObject(entries));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not save the original sizes of the NahjaUI interface.");
        }
    }

    private static string? CopyFile()
    {
        try
        {
            var file = Plugin.PluginInterface.ConfigFile;
            if (!file.Exists)
                return null;

            Directory.CreateDirectory(CopiesDirectory);
            var path = Path.Combine(CopiesDirectory, $"NahjaUI-before-resize-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            file.CopyTo(path, false);

            foreach (var old in new DirectoryInfo(CopiesDirectory).GetFiles("NahjaUI-before-resize-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(CopiesToKeep))
                old.Delete();

            return path;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not copy the NahjaUI settings before the resize.");
            return null;
        }
    }

    // Goes through the settings in place (the modules hold on to these objects), setting every number whose name says it is a size to
    // original * factor. The original is the remembered one, or (when there is none, or the user changed the number since) the current
    // value taken back to 100 %.
    private static void Walk(object? node, string path, double factor, int applied, Dictionary<string, Entry> old, Dictionary<string, Entry> fresh, HashSet<object> seen, ref int changed)
    {
        if (node is null)
            return;

        var type = node.GetType();
        if (type.IsValueType || node is string)
            return;

        // Only what belongs to the plugin and the lists and dictionaries that hold it.
        var isCollection = node is IEnumerable;
        if (!isCollection && type.Namespace?.StartsWith("NahjaUI", StringComparison.Ordinal) != true)
            return;

        if (!seen.Add(node))
            return;

        if (node is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                Walk(entry.Value, path + "/" + entry.Key, factor, applied, old, fresh, seen, ref changed);

            return;
        }

        if (node is IEnumerable list)
        {
            var index = 0;
            foreach (var item in list)
            {
                // A list item with an id is found by it (the lists change order); any other by its place.
                var id = item?.GetType().GetProperty("Id")?.GetValue(item) as string;
                Walk(item, path + "[" + (id ?? index.ToString()) + "]", factor, applied, old, fresh, seen, ref changed);
                index++;
            }

            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead || Skipped.Contains(property.Name))
                continue;

            if (property.IsDefined(typeof(JsonIgnoreAttribute), true))
                continue;

            var propertyType = property.PropertyType;
            var key = path + "/" + property.Name;

            if (propertyType == typeof(int) || propertyType == typeof(float))
            {
                if (!property.CanWrite || !(SizeName.IsMatch(property.Name) || TextName.IsMatch(property.Name)))
                    continue;

                var current = Convert.ToDouble(property.GetValue(node));
                if (current == 0d)
                    continue;

                double original;
                if (old.TryGetValue(key, out var known) && current == known.Set)
                    original = known.Original;
                else
                    original = current * 100.0 / applied;

                var scaled = Scaled(property.Name, original, factor, propertyType == typeof(int));

                if (scaled != current)
                {
                    property.SetValue(node, propertyType == typeof(int) ? (object)(int)scaled : (float)scaled);
                    changed++;
                }

                fresh[key] = new Entry { Original = original, Set = scaled };
            }
            else if (!propertyType.IsValueType && propertyType != typeof(string))
            {
                Walk(property.GetValue(node), key, factor, applied, old, fresh, seen, ref changed);
            }
        }
    }

    private static double Scaled(string name, double original, double factor, bool isInteger)
    {
        // Back at 100 %: the original, exactly as it was.
        if (Math.Abs(factor - 1d) < 1e-9)
            return isInteger ? Math.Round(original, MidpointRounding.AwayFromZero) : original;

        var isText = TextName.IsMatch(name);
        var value = original * factor;

        // A whole number stays whole; a number with a decimal part (an offset set with halves of a pixel) keeps its halves.
        value = isInteger || original == Math.Floor(original) ? Math.Round(value, MidpointRounding.AwayFromZero) : Math.Round(value * 2d) / 2d;

        if (isText)
            return Math.Clamp(value, 6d, 72d);

        if (WidthName.IsMatch(name))
            return Math.Clamp(value, 1d, 4000d);

        if (HeightName.IsMatch(name))
            return Math.Clamp(value, 1d, 1000d);

        return value;
    }
}
