using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace NahjaUI.Game;

/// <summary>A player action (ability) found by a search or listed for a job.</summary>
internal sealed record ActionInfo(uint Id, string Name, int Level, float RecastSeconds, int MaxCharges);

/// <summary>A status found by a name search.</summary>
/// <param name="Category">1 = beneficial (buff), 2 = detrimental (debuff), 0 = other.</param>
internal sealed record StatusSearchResult(uint Id, string Name, byte Category);

/// <summary>
/// Turns ids into readable names and icons (statuses, jobs) using the game's own data.
/// Results are cached, so each id is looked up only once.
/// </summary>
internal static class GameNames
{
    private static readonly Dictionary<uint, string> StatusNames = new();
    private static readonly Dictionary<uint, uint> StatusIcons = new();
    private static readonly Dictionary<uint, int> StatusMaxStacks = new();
    private static readonly Dictionary<uint, byte> StatusCategories = new();
    private static readonly Dictionary<uint, string> JobNames = new();
    private static readonly Dictionary<uint, string> ActionNames = new();
    private static readonly Dictionary<uint, uint> ActionIcons = new();
    private static readonly Dictionary<(byte Type, uint Id), string> CastNames = new();
    private static readonly Dictionary<(byte Type, uint Id), uint> CastIcons = new();

    public static string StatusName(uint statusId)
    {
        if (StatusNames.TryGetValue(statusId, out var cached))
            return cached;

        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            name = row.Name.ToString();

        if (string.IsNullOrEmpty(name))
            name = $"Status {statusId}";

        StatusNames[statusId] = name;
        return name;
    }

    /// <summary>The icon id of a status, or 0 if it has none.</summary>
    public static uint StatusIconId(uint statusId)
    {
        if (StatusIcons.TryGetValue(statusId, out var cached))
            return cached;

        uint icon = 0;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            icon = (uint)row.Icon;

        StatusIcons[statusId] = icon;
        return icon;
    }

    /// <summary>1 = beneficial (buff), 2 = detrimental (debuff), 0 = other or unknown.</summary>
    public static byte StatusCategory(uint statusId)
    {
        if (StatusCategories.TryGetValue(statusId, out var cached))
            return cached;

        byte category = 0;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            category = (byte)row.StatusCategory;

        StatusCategories[statusId] = category;
        return category;
    }

    /// <summary>How many stacks a status can have. 0 or 1 means it does not stack.</summary>
    public static int MaxStacks(uint statusId)
    {
        if (StatusMaxStacks.TryGetValue(statusId, out var cached))
            return cached;

        var max = 0;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            max = (int)row.MaxStacks;

        StatusMaxStacks[statusId] = max;
        return max;
    }

    /// <summary>Finds statuses whose name contains the text (not case sensitive). Skips unnamed ones.</summary>
    public static IReadOnlyList<StatusSearchResult> SearchStatuses(string query, int maxResults)
    {
        var results = new List<StatusSearchResult>();
        if (string.IsNullOrWhiteSpace(query))
            return results;

        foreach (var row in Plugin.DataManager.GetExcelSheet<Status>())
        {
            var name = row.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            if (!name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new StatusSearchResult(row.RowId, name, (byte)row.StatusCategory));
            if (results.Count >= maxResults)
                break;
        }

        return results;
    }

    // ---------------------------------------------------------------- actions

    public static string ActionName(uint actionId)
    {
        if (ActionNames.TryGetValue(actionId, out var cached))
            return cached;

        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row))
            name = row.Name.ToString();

        if (string.IsNullOrEmpty(name))
            name = $"Action {actionId}";

        ActionNames[actionId] = name;
        return name;
    }

    /// <summary>The icon id of an action, or 0 if it has none.</summary>
    public static uint ActionIconId(uint actionId)
    {
        if (ActionIcons.TryGetValue(actionId, out var cached))
            return cached;

        uint icon = 0;
        if (Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row))
            icon = row.Icon;

        ActionIcons[actionId] = icon;
        return icon;
    }

    /// <summary>
    /// The abilities of a job that have a real cooldown (longer than a global cooldown) and are unlocked
    /// at the given level, in the order they are learned. Role actions are not included: use the search for them.
    /// </summary>
    public static IReadOnlyList<ActionInfo> AbilitiesOfJob(uint jobId, int level)
    {
        // Jobs inherit the actions of their starting class (White Mage uses Conjurer actions).
        uint parentId = 0;
        if (Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var jobRow))
            parentId = jobRow.ClassJobParent.RowId;

        var best = new Dictionary<string, ActionInfo>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<ActionSheet>())
        {
            if (!row.IsPlayerAction || row.IsPvP)
                continue;

            var owner = row.ClassJob.RowId;
            if (owner != jobId && (parentId == 0 || owner != parentId))
                continue;

            if (row.ClassJobLevel > level)
                continue;

            // Recast100ms is in tenths of a second: 35 is the length of a global cooldown.
            if (row.Recast100ms <= 35)
                continue;

            var name = row.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            var info = new ActionInfo(row.RowId, name, row.ClassJobLevel, row.Recast100ms / 10f, Math.Max(1, (int)row.MaxCharges));

            // Several rows can share a name (older versions): keep the newest.
            if (!best.TryGetValue(name, out var existing) || existing.Level < info.Level)
                best[name] = info;
        }

        return best.Values.OrderBy(a => a.Level).ThenBy(a => a.Name).ToList();
    }

    /// <summary>Finds player actions whose name contains the text (not case sensitive).</summary>
    public static IReadOnlyList<ActionInfo> SearchActions(string query, int maxResults)
    {
        var results = new List<ActionInfo>();
        if (string.IsNullOrWhiteSpace(query))
            return results;

        foreach (var row in Plugin.DataManager.GetExcelSheet<ActionSheet>())
        {
            if (!row.IsPlayerAction || row.IsPvP)
                continue;

            var name = row.Name.ToString();
            if (string.IsNullOrEmpty(name) || !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new ActionInfo(row.RowId, name, row.ClassJobLevel, row.Recast100ms / 10f, Math.Max(1, (int)row.MaxCharges)));
            if (results.Count >= maxResults)
                break;
        }

        return results;
    }

    // ---------------------------------------------------------------- what is being cast

    /// <summary>
    /// The name of what a character is casting. Spells and abilities have their own sheet; items, general actions
    /// and mounts are handled too. Anything else is just called "Casting".
    /// </summary>
    public static string CastName(byte actionType, uint id)
    {
        if (actionType == 1)
            return ActionName(id);

        if (CastNames.TryGetValue((actionType, id), out var cached))
            return cached;

        var name = string.Empty;

        switch (actionType)
        {
            case 2: // an item (high quality items add 1,000,000 to the id)
                if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(id % 1_000_000, out var item))
                    name = item.Name.ToString();
                break;

            case 5: // a general action
                if (Plugin.DataManager.GetExcelSheet<GeneralAction>().TryGetRow(id, out var general))
                    name = general.Name.ToString();
                break;

            case 13: // a mount
                if (Plugin.DataManager.GetExcelSheet<Mount>().TryGetRow(id, out var mount))
                {
                    var singular = mount.Singular.ToString();
                    name = singular.Length > 0 ? char.ToUpperInvariant(singular[0]) + singular[1..] : string.Empty;
                }
                break;
        }

        if (string.IsNullOrEmpty(name))
            name = "Casting";

        CastNames[(actionType, id)] = name;
        return name;
    }

    /// <summary>The icon id of what a character is casting, or 0 if there is none.</summary>
    public static uint CastIconId(byte actionType, uint id)
    {
        if (actionType == 1)
            return ActionIconId(id);

        if (CastIcons.TryGetValue((actionType, id), out var cached))
            return cached;

        uint icon = 0;

        switch (actionType)
        {
            case 2:
                if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(id % 1_000_000, out var item))
                    icon = item.Icon;
                break;

            case 5:
                if (Plugin.DataManager.GetExcelSheet<GeneralAction>().TryGetRow(id, out var general))
                    icon = (uint)general.Icon;
                break;

            case 13:
                if (Plugin.DataManager.GetExcelSheet<Mount>().TryGetRow(id, out var mount))
                    icon = mount.Icon;
                break;
        }

        CastIcons[(actionType, id)] = icon;
        return icon;
    }

    public static string JobAbbreviation(uint jobId)
    {
        if (JobNames.TryGetValue(jobId, out var cached))
            return cached;

        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var row))
            name = row.Abbreviation.ToString();

        if (string.IsNullOrEmpty(name))
            name = "???";

        JobNames[jobId] = name;
        return name;
    }
}
