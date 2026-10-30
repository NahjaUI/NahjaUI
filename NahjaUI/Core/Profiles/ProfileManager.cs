using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.Auras;
using NahjaUI.Modules.CastBars;
using NahjaUI.Modules.JobGauges;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Core.Profiles;

/// <summary>The parts of the configuration that can be exported and imported on their own.</summary>
internal enum ProfileBlock
{
    Auras,
    Placement,
    UnitFrames,
    CastBars,
    JobGauges,
    Appearance
}

/// <summary>What to do with the auras of an import.</summary>
internal enum AuraImportMode
{
    /// <summary>Keep my auras and add the imported ones (they get new ids, and no positions).</summary>
    Add,

    /// <summary>Replace all my auras with the imported ones (a full restore, positions included).</summary>
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
        ProfileBlock.Auras => "Auras and groups",
        ProfileBlock.Placement => "Position and size of every element",
        ProfileBlock.UnitFrames => "Unit frames",
        ProfileBlock.CastBars => "Cast bars",
        ProfileBlock.JobGauges => "Job gauges",
        ProfileBlock.Appearance => "Appearance (accent color, target ring color, menu text size)",
        _ => block.ToString()
    };

    private static string Key(ProfileBlock block) => block switch
    {
        ProfileBlock.Auras => "auras",
        ProfileBlock.Placement => "placement",
        ProfileBlock.UnitFrames => "unitFrames",
        ProfileBlock.CastBars => "castBars",
        ProfileBlock.JobGauges => "jobGauges",
        ProfileBlock.Appearance => "appearance",
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
                ProfileBlock.Auras => new JObject
                {
                    ["auras"] = JToken.FromObject(config.Auras, Serializer),
                    ["groups"] = JToken.FromObject(config.AuraGroups, Serializer),
                    ["rootOrder"] = JToken.FromObject(config.RootOrder, Serializer)
                },
                ProfileBlock.Placement => JToken.FromObject(config.Layouts, Serializer),
                ProfileBlock.UnitFrames => JToken.FromObject(config.UnitFrames, Serializer),
                ProfileBlock.CastBars => JToken.FromObject(config.CastBars, Serializer),
                ProfileBlock.JobGauges => JToken.FromObject(config.JobGauges, Serializer),
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
            case ProfileBlock.Auras:
                var auras = (token["auras"] as JArray)?.Count ?? 0;
                var groups = (token["groups"] as JArray)?.Count ?? 0;
                return $"{Label(block)}: {auras} aura(s) in {groups} group(s)";

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
    public string Apply(ProfilePreview preview, ISet<ProfileBlock> chosen, AuraImportMode auraMode, out bool ok)
    {
        ok = false;

        if (!preview.Ok || preview.Blocks is null)
            return preview.Error;

        var config = plugin.Configuration;
        var blocks = preview.Blocks;

        try
        {
            // 1. Read everything that was chosen. Nothing is changed until all of it has been read without problems.
            AuraBlock? auras = null;
            Dictionary<string, ElementLayout>? placement = null;
            UnitFramesSettings? unitFrames = null;
            CastBarsSettings? castBars = null;
            JobGaugesSettings? jobGauges = null;
            JObject? appearance = null;

            if (chosen.Contains(ProfileBlock.Auras) && blocks[Key(ProfileBlock.Auras)] is JObject auraToken)
            {
                auras = new AuraBlock(
                    auraToken["auras"]?.ToObject<List<AuraDefinition>>(Serializer) ?? new List<AuraDefinition>(),
                    auraToken["groups"]?.ToObject<List<AuraGroupDefinition>>(Serializer) ?? new List<AuraGroupDefinition>(),
                    auraToken["rootOrder"]?.ToObject<List<string>>(Serializer) ?? new List<string>());
            }

            if (chosen.Contains(ProfileBlock.Placement) && blocks[Key(ProfileBlock.Placement)] is JObject placementToken)
                placement = placementToken.ToObject<Dictionary<string, ElementLayout>>(Serializer);

            if (chosen.Contains(ProfileBlock.UnitFrames) && blocks[Key(ProfileBlock.UnitFrames)] is JToken uf)
                unitFrames = uf.ToObject<UnitFramesSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.CastBars) && blocks[Key(ProfileBlock.CastBars)] is JToken cb)
                castBars = cb.ToObject<CastBarsSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.JobGauges) && blocks[Key(ProfileBlock.JobGauges)] is JToken jg)
                jobGauges = jg.ToObject<JobGaugesSettings>(Serializer);

            if (chosen.Contains(ProfileBlock.Appearance) && blocks[Key(ProfileBlock.Appearance)] is JObject ap)
                appearance = ap;

            // 2. Save what there is now, so any import can be undone.
            var backupPath = SaveToFile(BackupDirectory, "backup", Export(Enum.GetValues<ProfileBlock>()));
            TrimBackups();

            // 3. Settings blocks: they are read from the configuration every frame, so replacing them is enough.
            if (unitFrames is not null)
                config.UnitFrames = unitFrames;

            if (castBars is not null)
                config.CastBars = castBars;

            if (jobGauges is not null)
                config.JobGauges = jobGauges;

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

            // 4. Auras.
            if (auras is not null)
            {
                if (auraMode == AuraImportMode.Replace)
                    ReplaceAuras(auras);
                else
                    AddAuras(auras);
            }

            // 5. Positions. Elements that already exist are updated in place, because the modules hold on to them.
            if (placement is not null)
            {
                var skipAuraKeys = auras is not null && auraMode == AuraImportMode.Add;

                foreach (var (id, imported) in placement)
                {
                    if (skipAuraKeys && IsAuraElementId(id))
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
            if (auras is not null)
                plugin.AuraSystem.ReloadFromConfiguration();

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

    private sealed record AuraBlock(List<AuraDefinition> Auras, List<AuraGroupDefinition> Groups, List<string> RootOrder);

    private static bool IsAuraElementId(string id) =>
        id.StartsWith("aura.", StringComparison.Ordinal) || id.StartsWith("aura-group.", StringComparison.Ordinal);

    // A full restore: the imported auras take the place of mine and keep their ids, so their positions match.
    private void ReplaceAuras(AuraBlock block)
    {
        var config = plugin.Configuration;

        config.Auras = block.Auras;
        config.AuraGroups = block.Groups;
        config.RootOrder = block.RootOrder;

        // Forget the positions of auras and groups that no longer exist.
        var valid = new HashSet<string>();
        foreach (var aura in block.Auras)
            valid.Add($"aura.{aura.Id}");
        foreach (var group in block.Groups)
            valid.Add($"aura-group.{group.Id}");

        foreach (var key in config.Layouts.Keys.Where(k => IsAuraElementId(k) && !valid.Contains(k)).ToList())
            config.Layouts.Remove(key);
    }

    // Shared auras: they are added after mine, with new ids so they can never clash with mine. They have no position,
    // so each one starts in the default place and the user puts it where they like.
    private void AddAuras(AuraBlock block)
    {
        var config = plugin.Configuration;

        var groupIds = new Dictionary<string, string>();
        foreach (var group in block.Groups)
        {
            var oldId = group.Id;
            group.Id = Guid.NewGuid().ToString("N");
            groupIds[oldId] = group.Id;
            config.AuraGroups.Add(group);
        }

        var auraIds = new Dictionary<string, string>();
        foreach (var aura in block.Auras)
        {
            var oldId = aura.Id;
            aura.Id = Guid.NewGuid().ToString("N");
            auraIds[oldId] = aura.Id;

            aura.GroupId = aura.GroupId is not null && groupIds.TryGetValue(aura.GroupId, out var newGroup) ? newGroup : null;
            config.Auras.Add(aura);
        }

        // The order of the explorer list: the imported entries go after mine.
        foreach (var key in block.RootOrder)
        {
            if (key.StartsWith("group:", StringComparison.Ordinal) && groupIds.TryGetValue(key["group:".Length..], out var g))
                config.RootOrder.Add($"group:{g}");
            else if (key.StartsWith("aura:", StringComparison.Ordinal) && auraIds.TryGetValue(key["aura:".Length..], out var a))
                config.RootOrder.Add($"aura:{a}");
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
