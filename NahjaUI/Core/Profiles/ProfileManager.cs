using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.Trackers;
using NahjaUI.Modules.ProgressBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Core.Profiles;

/// <summary>The parts of the configuration that can be exported and imported on their own.</summary>
internal enum ProfileBlock
{
    Trackers,
    Placement,
    UnitFrames,
    ProgressBars,
    JobGauges,
    Appearance,
    Filters,
    NamePlates,
    EnmityList,
    StatusEffects,
    Tooltip
}

/// <summary>What to do with the trackers of an import.</summary>
internal enum TrackerImportMode
{
    /// <summary>Keep my trackers and add the imported ones (they get new ids, and no positions).</summary>
    Add,

    /// <summary>Replace all my trackers with the imported ones (a full restore, positions included).</summary>
    Replace
}

/// <summary>The result of reading an export code, before anything is changed.</summary>
internal sealed class ProfilePreview
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
    public JObject? Blocks { get; init; }
    public HashSet<ProfileBlock> Present { get; init; } = new();
    public List<string> Summary { get; init; } = new();
}

/// <summary>
/// Turns the configuration (or parts of it) into a text code and back. The code is plain data: JSON, compressed with
/// gzip and written as base64, with a "NUI1:" prefix. Nothing in it is code, and a damaged code only gives an error message.
/// Before anything is imported, the current configuration is saved to a backup file.
/// </summary>
internal sealed class ProfileManager
{
    private const string Prefix = "NUI1:";
    private const int FormatVersion = 1;

    // A code never needs more than this once unpacked (protects against a tiny code that unpacks to gigabytes).
    private const int MaxJsonBytes = 16 * 1024 * 1024;

    private const int BackupsToKeep = 20;

    // Imported objects start empty instead of from their defaults, so lists are not merged with the defaults.
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace
    });

    private readonly Plugin plugin;

    public ProfileManager(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public static string BackupDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "backups");

    public static string ExportDirectory => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "exports");

    public static string Label(ProfileBlock block) => block switch
    {
        ProfileBlock.Trackers => "Trackers and groups",
        ProfileBlock.Placement => "Position and size of every element",
        ProfileBlock.UnitFrames => "Unit frames",
        ProfileBlock.ProgressBars => "Progress bars",
        ProfileBlock.JobGauges => "Job gauges",
        ProfileBlock.Appearance => "Appearance (accent color, target ring color, menu text size)",
        ProfileBlock.Filters => "Filters (the named lists of statuses)",
        ProfileBlock.NamePlates => "Nameplates",
        ProfileBlock.EnmityList => "Enmity list",
        ProfileBlock.StatusEffects => "Status effects (your own enhancements and enfeeblements)",
        ProfileBlock.Tooltip => "Tooltip",
        _ => block.ToString()
    };

    private static string Key(ProfileBlock block) => block switch
    {
        ProfileBlock.Trackers => "trackers",
        ProfileBlock.Placement => "placement",
        ProfileBlock.UnitFrames => "unitFrames",
        ProfileBlock.ProgressBars => "progressBars",
        ProfileBlock.JobGauges => "jobGauges",
        ProfileBlock.Appearance => "appearance",
        ProfileBlock.Filters => "filters",
        ProfileBlock.NamePlates => "namePlates",
        ProfileBlock.EnmityList => "enmityList",
        ProfileBlock.StatusEffects => "statusEffects",
        ProfileBlock.Tooltip => "tooltip",
        _ => block.ToString()
    };

    // ---------------------------------------------------------------- export

    /// <summary>Builds the code for the chosen parts of the configuration.</summary>
    public string Export(IEnumerable<ProfileBlock> blocks)
    {
        var config = plugin.Configuration;
        var data = new JObject();

        foreach (var block in blocks)
        {
            data[Key(block)] = block switch
            {
                ProfileBlock.Trackers => new JObject
                {
                    ["trackers"] = JToken.FromObject(config.Trackers, Serializer),
                    ["groups"] = JToken.FromObject(config.TrackerGroups, Serializer),
                    ["rootOrder"] = JToken.FromObject(config.RootOrder, Serializer)
                },
                ProfileBlock.Placement => JToken.FromObject(config.Layouts, Serializer),
                ProfileBlock.UnitFrames => JToken.FromObject(config.UnitFrames, Serializer),
                ProfileBlock.ProgressBars => JToken.FromObject(config.ProgressBars, Serializer),
                ProfileBlock.JobGauges => JToken.FromObject(config.JobGauges, Serializer),
                ProfileBlock.Filters => JToken.FromObject(config.Filters, Serializer),
                ProfileBlock.NamePlates => JToken.FromObject(config.NamePlates, Serializer),
                ProfileBlock.EnmityList => JToken.FromObject(config.EnmityList, Serializer),
                ProfileBlock.StatusEffects => JToken.FromObject(config.StatusEffects, Serializer),
                ProfileBlock.Tooltip => JToken.FromObject(config.Tooltip, Serializer),
                ProfileBlock.Appearance => new JObject
                {
                    ["accent"] = JToken.FromObject(config.AccentColor, Serializer),
                    ["targetRing"] = JToken.FromObject(config.TargetRingColor, Serializer),
                    ["menuTextSize"] = config.MenuTextSize,
                    ["windowRounding"] = config.WindowRoundingPx
                },
                _ => JValue.CreateNull()
            };
        }

        var root = new JObject
        {
            ["format"] = "NahjaUI",
            ["version"] = FormatVersion,
            ["created"] = DateTime.UtcNow.ToString("o"),
            ["blocks"] = data
        };

        var bytes = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            gzip.Write(bytes, 0, bytes.Length);

        return Prefix + Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Writes a code to a text file and returns the path.</summary>
    public string SaveToFile(string directory, string namePrefix, string code)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{namePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, code);
        return path;
    }

    // ---------------------------------------------------------------- reading a code

    /// <summary>Reads a code and describes what is inside. Never throws and never changes anything.</summary>
    public ProfilePreview Read(string text)
    {
        try
        {
            // Chat apps may add spaces or line breaks.
            var clean = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

            if (!clean.StartsWith(Prefix, StringComparison.Ordinal))
                return Fail("That is not a NahjaUI code (it should start with NUI1:).");

            var packed = Convert.FromBase64String(clean[Prefix.Length..]);

            string json;
            using (var input = new MemoryStream(packed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > MaxJsonBytes)
                        return Fail("That code is too big to be a NahjaUI profile.");
                }

                json = Encoding.UTF8.GetString(output.ToArray());
            }

            // Codes made before the cast bars were renamed to progress bars.
            json = ConfigRename.Apply(json);
            var root = JObject.Parse(json);

            if ((string?)root["format"] != "NahjaUI")
                return Fail("That code was not made by NahjaUI.");

            var version = (int?)root["version"] ?? 0;
            if (version > FormatVersion)
                return Fail("That code was made with a newer version of NahjaUI. Update the plugin first.");

            if (root["blocks"] is not JObject blocks)
                return Fail("That code has nothing inside.");

            var present = new HashSet<ProfileBlock>();
            var summary = new List<string>();

            foreach (var block in Enum.GetValues<ProfileBlock>())
            {
                if (blocks[Key(block)] is null || blocks[Key(block)]!.Type == JTokenType.Null)
                    continue;

                present.Add(block);
                summary.Add(Describe(block, blocks[Key(block)]!));
            }

            if (present.Count == 0)
                return Fail("That code has nothing inside.");

            return new ProfilePreview { Ok = true, Blocks = blocks, Present = present, Summary = summary };
        }
        catch (Exception)
        {
            return Fail("That code is damaged or incomplete. Copy it again, whole.");
        }
    }

    private static ProfilePreview Fail(string message) => new() { Ok = false, Error = message };

    private static string Describe(ProfileBlock block, JToken token)
    {
        switch (block)
        {
            case ProfileBlock.Trackers:
                var trackers = (token["trackers"] as JArray)?.Count ?? 0;
                var groups = (token["groups"] as JArray)?.Count ?? 0;
                return $"{Label(block)}: {trackers} tracker(s) in {groups} group(s)";

            case ProfileBlock.Placement:
                return $"{Label(block)}: {(token as JObject)?.Count ?? 0} element(s)";

            default:
                return Label(block);
        }
    }

    // ---------------------------------------------------------------- import

    /// <summary>
    /// Applies the chosen parts of a code. The current configuration is saved to a backup file first.
    /// Returns a message for the user and sets <paramref name="ok"/>.
    /// </summary>
    public string Apply(ProfilePreview preview, ISet<ProfileBlock> chosen, TrackerImportMode trackerMode, out bool ok)
    {
        ok = false;

        if (!preview.Ok || preview.Blocks is null)
            return preview.Error;

        var config = plugin.Configuration;
        var blocks = preview.Blocks;

        try
        {
            // 1. Read everything that was chosen. Nothing is changed until all of it has been read without problems.
            TrackerBlock? trackers = null;
            Dictionary<string, ElementLayout>? placement = null;
            UnitFramesSettings? unitFrames = null;
            ProgressBarsSettings? progressBars = null;
            JobGaugesSettings? jobGauges = null;
            JObject? appearance = null;
            NahjaUI.Modules.Filters.FiltersSettings? filters = null;
            NahjaUI.Modules.NamePlates.NamePlateSettings? namePlates = null;
            NahjaUI.Modules.EnmityList.EnmityListSettings? enmityList = null;
            NahjaUI.Modules.StatusEffects.StatusEffectsSettings? statusEffects = null;
            NahjaUI.Modules.Tooltip.TooltipSettings? tooltip = null;

            if (chosen.Contains(ProfileBlock.Trackers) && blocks[Key(ProfileBlock.Trackers)] is JObject trackerToken)
            {
                trackers = new TrackerBlock(
                    trackerToken["trackers"]?.ToObject<List<TrackerDefinition>>(Serializer) ?? new List<TrackerDefinition>(),
                    trackerToken["groups"]?.ToObject<List<TrackerGroupDefinition>>(Serializer) ?? new List<TrackerGroupDefinition>(),
                    trackerToken["rootOrder"]?.ToObject<List<string>>(Serializer) ?? new List<string>());
            }

            if (chosen.Contains(ProfileBlock.Placement) && blocks[Key(ProfileBlock.Placement)] is JObject placementToken)
                placement = placementToken.ToObject<Dictionary<string, ElementLayout>>(Serializer);

            if (chosen.Contains(ProfileBlock.UnitFrames) && blocks[Key(ProfileBlock.UnitFrames)] is JToken uf)
                unitFrames = uf.ToObject<UnitFramesSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.ProgressBars) && blocks[Key(ProfileBlock.ProgressBars)] is JToken cb)
                progressBars = cb.ToObject<ProgressBarsSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.JobGauges) && blocks[Key(ProfileBlock.JobGauges)] is JToken jg)
                jobGauges = jg.ToObject<JobGaugesSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.Appearance) && blocks[Key(ProfileBlock.Appearance)] is JObject ap)
                appearance = ap;

            if (chosen.Contains(ProfileBlock.Filters) && blocks[Key(ProfileBlock.Filters)] is JToken fl)
                filters = fl.ToObject<NahjaUI.Modules.Filters.FiltersSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.NamePlates) && blocks[Key(ProfileBlock.NamePlates)] is JToken np)
                namePlates = np.ToObject<NahjaUI.Modules.NamePlates.NamePlateSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.EnmityList) && blocks[Key(ProfileBlock.EnmityList)] is JToken el)
                enmityList = el.ToObject<NahjaUI.Modules.EnmityList.EnmityListSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.StatusEffects) && blocks[Key(ProfileBlock.StatusEffects)] is JToken se)
                statusEffects = se.ToObject<NahjaUI.Modules.StatusEffects.StatusEffectsSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.Tooltip) && blocks[Key(ProfileBlock.Tooltip)] is JToken tt)
                tooltip = tt.ToObject<NahjaUI.Modules.Tooltip.TooltipSettings>(Serializer);

            // 2. Save what there is now, so any import can be undone.
            var backupPath = SaveToFile(BackupDirectory, "backup", Export(Enum.GetValues<ProfileBlock>()));
            TrimBackups();

            // 3. Settings blocks: they are read from the configuration every frame, so replacing them is enough.
            if (unitFrames is not null)
                config.UnitFrames = unitFrames;

            if (progressBars is not null)
                config.ProgressBars = progressBars;

            if (jobGauges is not null)
                config.JobGauges = jobGauges;

            if (filters is not null)
                config.Filters = filters;

            if (namePlates is not null)
                config.NamePlates = namePlates;

            if (enmityList is not null)
                config.EnmityList = enmityList;

            if (statusEffects is not null)
                config.StatusEffects = statusEffects;

            if (tooltip is not null)
                config.Tooltip = tooltip;

            if (appearance is not null)
            {
                if (appearance["accent"] is JToken accent && accent.ToObject<System.Numerics.Vector4>(Serializer) is var color)
                    config.AccentColor = new System.Numerics.Vector4(color.X, color.Y, color.Z, 1f);

                if (appearance["targetRing"] is JToken ring && ring.ToObject<System.Numerics.Vector4>(Serializer) is var ringColor)
                    config.TargetRingColor = new System.Numerics.Vector4(ringColor.X, ringColor.Y, ringColor.Z, 1f);

                var size = (int?)appearance["menuTextSize"];
                if (size is not null)
                    config.MenuTextSize = Math.Clamp(size.Value, 8, 40);

                var rounding = (int?)appearance["windowRounding"];
                if (rounding is not null)
                    config.WindowRoundingPx = Math.Clamp(rounding.Value, 0, 30);
            }

            // 4. Trackers.
            if (trackers is not null)
            {
                if (trackerMode == TrackerImportMode.Replace)
                    ReplaceTrackers(trackers);
                else
                    AddTrackers(trackers);
            }

            // 5. Positions. Elements that already exist are updated in place, because the modules hold on to them.
            if (placement is not null)
            {
                var skipTrackerKeys = trackers is not null && trackerMode == TrackerImportMode.Add;

                foreach (var (id, imported) in placement)
                {
                    if (skipTrackerKeys && IsTrackerElementId(id))
                        continue;

                    if (config.Layouts.TryGetValue(id, out var existing))
                    {
                        existing.CopyPositionFrom(imported);
                        existing.Locked = imported.Locked;
                    }
                    else
                    {
                        config.Layouts[id] = imported;
                    }
                }
            }

            // 6. Rebuild what is drawn from the new configuration.
            if (trackers is not null)
                plugin.TrackerSystem.ReloadFromConfiguration();

            plugin.Layout.SelectedId = null;
            plugin.SaveNow();

            ok = true;
            return $"Imported. Your previous settings were saved to {backupPath}";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Importing a NahjaUI profile failed.");
            return "The import failed and nothing was changed. If the problem continues, ask the person who made the code to export it again.";
        }
    }

    private sealed record TrackerBlock(List<TrackerDefinition> Trackers, List<TrackerGroupDefinition> Groups, List<string> RootOrder);

    private static bool IsTrackerElementId(string id) =>
        id.StartsWith("tracker.", StringComparison.Ordinal) || id.StartsWith("tracker-group.", StringComparison.Ordinal);

    // A full restore: the imported trackers take the place of mine and keep their ids, so their positions match.
    private void ReplaceTrackers(TrackerBlock block)
    {
        var config = plugin.Configuration;

        config.Trackers = block.Trackers;
        config.TrackerGroups = block.Groups;
        config.RootOrder = block.RootOrder;

        // Forget the positions of trackers and groups that no longer exist.
        var valid = new HashSet<string>();
        foreach (var tracker in block.Trackers)
            valid.Add($"tracker.{tracker.Id}");
        foreach (var group in block.Groups)
            valid.Add($"tracker-group.{group.Id}");

        foreach (var key in config.Layouts.Keys.Where(k => IsTrackerElementId(k) && !valid.Contains(k)).ToList())
            config.Layouts.Remove(key);
    }

    // Shared trackers: they are added after mine, with new ids so they can never clash with mine. They have no position,
    // so each one starts in the default place and the user puts it where they like.
    private void AddTrackers(TrackerBlock block)
    {
        var config = plugin.Configuration;

        var groupIds = new Dictionary<string, string>();
        foreach (var group in block.Groups)
        {
            var oldId = group.Id;
            group.Id = Guid.NewGuid().ToString("N");
            groupIds[oldId] = group.Id;
            config.TrackerGroups.Add(group);
        }

        var trackerIds = new Dictionary<string, string>();
        foreach (var tracker in block.Trackers)
        {
            var oldId = tracker.Id;
            tracker.Id = Guid.NewGuid().ToString("N");
            trackerIds[oldId] = tracker.Id;

            tracker.GroupId = tracker.GroupId is not null && groupIds.TryGetValue(tracker.GroupId, out var newGroup) ? newGroup : null;
            config.Trackers.Add(tracker);
        }

        // The order of the explorer list: the imported entries go after mine.
        foreach (var key in block.RootOrder)
        {
            if (key.StartsWith("group:", StringComparison.Ordinal) && groupIds.TryGetValue(key["group:".Length..], out var g))
                config.RootOrder.Add($"group:{g}");
            else if (key.StartsWith("tracker:", StringComparison.Ordinal) && trackerIds.TryGetValue(key["tracker:".Length..], out var a))
                config.RootOrder.Add($"tracker:{a}");
        }
    }

    // ---------------------------------------------------------------- backups

    /// <summary>The backup files, newest first.</summary>
    public IReadOnlyList<FileInfo> ListBackups()
    {
        try
        {
            if (!Directory.Exists(BackupDirectory))
                return Array.Empty<FileInfo>();

            return new DirectoryInfo(BackupDirectory)
                .GetFiles("backup-*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private void TrimBackups()
    {
        try
        {
            foreach (var old in ListBackups().Skip(BackupsToKeep))
                old.Delete();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not delete old NahjaUI backups.");
        }
    }
}
