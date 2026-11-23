using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace NahjaUI.Game;

/// <summary>A player action (ability) found by a search or listed for a job.</summary>
internal sealed record ActionInfo(uint Id, string Name, int Level, float RecastSeconds, int MaxCharges);

/// <summary>A status found by a name search.</summary>
/// <param name="Category">1 = beneficial (enhancement), 2 = detrimental (enfeeblement), 0 = other.</param>
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
    private static readonly Dictionary<uint, uint> ComboParents = new();
    private static readonly Dictionary<(byte Type, uint Id), string> CastNames = new();
    private static readonly Dictionary<(byte Type, uint Id), uint> CastIcons = new();

    private static readonly Dictionary<uint, string> StatusDescriptions = new();

    /// <summary>The description of a status as the game shows it (formatting codes resolved, plain text). Empty if it has none.</summary>
    public static string StatusDescription(uint statusId)
    {
        if (StatusDescriptions.TryGetValue(statusId, out var cached))
            return cached;

        var text = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
        {
            try
            {
                text = Plugin.SeStringEvaluator.Evaluate(row.Description).ExtractText();
            }
            catch
            {
                text = row.Description.ExtractText();
            }
        }

        StatusDescriptions[statusId] = text;
        return text;
    }

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

    /// <summary>1 = beneficial (enhancement), 2 = detrimental (enfeeblement), 0 = other or unknown.</summary>
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

    private static readonly System.Collections.Generic.Dictionary<uint, bool> StatusDispellable = new();

    /// <summary>The game's data says a cleanse (Esuna and the like) can remove this status.</summary>
    public static bool StatusCanDispel(uint statusId)
    {
        if (StatusDispellable.TryGetValue(statusId, out var cached))
            return cached;

        var dispellable = false;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            dispellable = row.CanDispel;

        StatusDispellable[statusId] = dispellable;
        return dispellable;
    }

    private static readonly System.Collections.Generic.Dictionary<uint, bool> StatusPermanent = new();

    /// <summary>The game's data says this status has no duration (it has no countdown).</summary>
    public static bool StatusIsPermanent(uint statusId)
    {
        if (StatusPermanent.TryGetValue(statusId, out var cached))
            return cached;

        var permanent = false;
        if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
            permanent = row.IsPermanent;

        StatusPermanent[statusId] = permanent;
        return permanent;
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

    private static readonly Dictionary<uint, int> ActionLevels = new();

    /// <summary>The level at which an action is learned (0 when unknown or not tied to a level).</summary>
    public static int ActionLevel(uint actionId)
    {
        if (ActionLevels.TryGetValue(actionId, out var cached))
            return cached;

        var level = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row)
            ? row.ClassJobLevel
            : 0;

        ActionLevels[actionId] = level;
        return level;
    }

    private static readonly Dictionary<(uint Job, uint Action), bool> ClassActions = new();

    // The unlock link (a quest or a game unlock) of each action that has one; 0 when it has none. The link is read from the table once.
    private static readonly Dictionary<uint, uint> ActionUnlockLinks = new();

    /// <summary>
    /// Has the character unlocked this action? Many actions need more than the level: Ley Lines, for instance, is learned by doing a quest.
    /// The game keeps the list of what the character has unlocked, so it is asked directly. Actions with no unlock requirement are always unlocked.
    /// </summary>
    public static bool ActionUnlocked(uint actionId)
    {
        if (!ActionUnlockLinks.TryGetValue(actionId, out var link))
        {
            link = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row) ? row.UnlockLink.RowId : 0u;
            ActionUnlockLinks[actionId] = link;
        }

        if (link == 0)
            return true;

        try
        {
            return Plugin.UnlockState.IsUnlockLinkUnlocked(link);
        }
        catch
        {
            // If the game cannot tell, the action is not hidden because of it.
            return true;
        }
    }

    /// <summary>
    /// Can the player use this action while on the given class or job? A base class (Thaumaturge) learns only the actions that belong to it;
    /// the actions of its job (Black Mage) are not available until the job is unlocked, even at the same level. A job can use every action
    /// of its starting class. Role and unowned actions are always allowed.
    /// </summary>
    public static bool ActionUsableBy(uint jobId, uint actionId)
    {
        // An action that has to be unlocked (usually by a quest) is not usable until the character has done it, whatever its level.
        if (!ActionUnlocked(actionId))
            return false;

        // A role action (Lucid Dreaming, Swiftcast...) is shared by every class of the role: the class that learns it at its level has it.
        if (IsRoleAction(actionId))
            return true;

        if (ClassActions.TryGetValue((jobId, actionId), out var cached))
            return cached;

        var usable = true;
        if (Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var action)
            && action.ClassJob.RowId != 0
            && Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var job))
        {
            // Only a base class (its own parent) is limited; a job keeps every action as before.
            var parent = job.ClassJobParent.RowId;
            if (parent == jobId)
                usable = action.ClassJob.RowId == jobId;
        }

        ClassActions[(jobId, actionId)] = usable;
        return usable;
    }

    /// <summary>True if the player's current class or job is the given job, or the starting class of the given job (Thaumaturge for Black Mage).</summary>
    public static bool JobMatches(uint playerJobId, uint trackerJobId)
    {
        if (playerJobId == trackerJobId)
            return true;

        // A tracker of the job loads on its starting class (Black Mage tracker on Thaumaturge), and a tracker of the class loads on its jobs.
        // A class with two jobs (Arcanist: Summoner and Scholar) only takes the trackers of the one it uses (see ProfileJobId: Summoner), not both.
        var sheet = Plugin.DataManager.GetExcelSheet<ClassJob>();
        return (sheet.TryGetRow(trackerJobId, out var trackerJob) && trackerJob.ClassJobParent.RowId == playerJobId && ProfileJobId(playerJobId) == trackerJobId)
            || (sheet.TryGetRow(playerJobId, out var playerJob) && playerJob.ClassJobParent.RowId == trackerJobId);
    }

    private const uint ArcanistId = 26;
    private const uint SummonerId = 27;
    private static readonly Dictionary<uint, uint> ProfileJobs = new();

    /// <summary>
    /// The job whose settings a class uses: Thaumaturge uses Black Mage's (the MP bar, the positions...), Conjurer White Mage's and so on.
    /// Arcanist uses Summoner's (a DPS) and not Scholar's. A job, or a class with no job, returns itself.
    /// </summary>
    public static uint ProfileJobId(uint jobId)
    {
        if (jobId == 0)
            return 0;

        if (ProfileJobs.TryGetValue(jobId, out var cached))
            return cached;

        var result = jobId;
        var sheet = Plugin.DataManager.GetExcelSheet<ClassJob>();

        // Only a starting class is its own parent.
        if (sheet.TryGetRow(jobId, out var row) && row.ClassJobParent.RowId == jobId)
        {
            if (jobId == ArcanistId)
            {
                result = SummonerId;
            }
            else
            {
                foreach (var other in sheet)
                {
                    if (other.RowId != jobId && other.ClassJobParent.RowId == jobId)
                    {
                        result = other.RowId;
                        break;
                    }
                }
            }
        }

        ProfileJobs[jobId] = result;
        return result;
    }

    private static readonly Dictionary<uint, bool> RoleActions = new();

    /// <summary>
    /// True for a role action (Rescue, Swiftcast, Lucid Dreaming, Feint...). Once learned, the game keeps them when the level is synced down
    /// (a FATE, a dungeon), so their level must not hide them.
    /// </summary>
    public static bool IsRoleAction(uint actionId)
    {
        if (RoleActions.TryGetValue(actionId, out var cached))
            return cached;

        var isRole = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row) && (row.IsRoleAction || (row.ClassJob.RowId == 0 && row.ClassJobLevel > 0 && row.IsPlayerAction));
        RoleActions[actionId] = isRole;
        return isRole;
    }

    // The classes and jobs that share the role actions of a role. Once a role action is learned with any of them, the others can use it.
    private static readonly uint[][] RoleGroups =
    {
        new uint[] { 1, 19, 3, 21, 32, 37 },                      // tanks
        new uint[] { 6, 24, 28, 33, 40 },                         // healers
        new uint[] { 2, 20, 4, 22, 29, 30, 34, 39, 41 },          // melee DPS
        new uint[] { 5, 23, 31, 38 },                             // physical ranged DPS
        new uint[] { 7, 25, 26, 27, 35, 42 },                     // magical ranged DPS
    };

    private static readonly Dictionary<(uint Job, uint Action), (long At, bool Learned)> RoleLearned = new();

    /// <summary>
    /// Has the character learned this role action? It is learned the first time any class or job of the role reaches its level, and from then on
    /// every class or job of the role can use it at any level (also synced down). The real level of each class is read from the game, so this
    /// does not depend on the level the player has right now. If it cannot be known, the action counts as learned.
    /// </summary>
    public static unsafe bool RoleActionLearned(uint jobId, uint actionId)
    {
        var now = Environment.TickCount64;
        if (RoleLearned.TryGetValue((jobId, actionId), out var cached) && now - cached.At < 1000)
            return cached.Learned;

        var learned = true;
        try
        {
            var group = RoleGroups.FirstOrDefault(g => g.Contains(jobId));
            var state = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            if (group is not null && state != null)
            {
                var needed = ActionLevel(actionId);
                var sheet = Plugin.DataManager.GetExcelSheet<ClassJob>();
                learned = false;
                foreach (var id in group)
                {
                    if (!sheet.TryGetRow(id, out var row) || row.ExpArrayIndex < 0)
                        continue;

                    if (state->ClassJobLevels[row.ExpArrayIndex] >= needed)
                    {
                        learned = true;
                        break;
                    }
                }
            }
        }
        catch
        {
            learned = true;
        }

        RoleLearned[(jobId, actionId)] = (now, learned);
        return learned;
    }

    /// <summary>The action that must be used right before this one for the combo to continue, or 0 if it is not a combo step.</summary>
    public static uint ComboParent(uint actionId)
    {
        if (ComboParents.TryGetValue(actionId, out var cached))
            return cached;

        var parent = Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row)
            ? row.ActionCombo.RowId
            : 0;

        ComboParents[actionId] = parent;
        return parent;
    }

    /// <summary>
    /// The first action of a chain of combo steps (Blade of Valor -> Blade of Truth -> Blade of Faith -> Confiteor gives Confiteor),
    /// or 0 when the action is not a combo step.
    /// </summary>
    public static uint ComboRoot(uint actionId)
    {
        var current = actionId;
        for (var i = 0; i < 8; i++)
        {
            var parent = ComboParent(current);
            if (parent == 0)
                return current == actionId ? 0 : current;

            current = parent;
        }

        return 0;
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
    // Actions every job has (general actions that are not marked as player actions in the game's data): Sprint.
    /// <summary>The id of Sprint, the action every job has.</summary>
    public const uint SprintId = 3;

    private static readonly HashSet<uint> UniversalActionIds = new() { SprintId };

    public static IReadOnlyList<ActionInfo> AbilitiesOfJob(uint jobId, int level)
    {
        // Jobs inherit the actions of their starting class (White Mage uses Conjurer actions).
        uint parentId = 0;
        if (Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(jobId, out var jobRow))
            parentId = jobRow.ClassJobParent.RowId;

        var best = new Dictionary<string, ActionInfo>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<ActionSheet>())
        {
            var universal = UniversalActionIds.Contains(row.RowId);

            if ((!row.IsPlayerAction && !universal) || row.IsPvP)
                continue;

            var owner = row.ClassJob.RowId;
            if (!universal && owner != jobId && (parentId == 0 || owner != parentId))
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

    /// <summary>
    /// Finds actions whose name contains the text (not case sensitive). Actions of a job come first. Actions that cannot be put
    /// on a hotbar (the follow-ups Blade of Faith, Truth and Valor) are included: they can still be tracked as combo steps.
    /// </summary>
    public static IReadOnlyList<ActionInfo> SearchActions(string query, int maxResults)
    {
        var results = new List<(bool OfAJob, ActionInfo Info)>();
        if (string.IsNullOrWhiteSpace(query))
            return new List<ActionInfo>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<ActionSheet>())
        {
            if (row.IsPvP)
                continue;

            var name = row.Name.ToString();
            if (string.IsNullOrEmpty(name) || !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            var ofAJob = row.IsPlayerAction || UniversalActionIds.Contains(row.RowId) || (row.ClassJob.RowId != 0 && row.ClassJobLevel > 0);
            results.Add((ofAJob, new ActionInfo(row.RowId, name, row.ClassJobLevel, row.Recast100ms / 10f, Math.Max(1, (int)row.MaxCharges))));
        }

        return results
            .OrderByDescending(entry => entry.OfAJob)
            .ThenBy(entry => entry.Info.Name.Length)
            .Take(maxResults)
            .Select(entry => entry.Info)
            .ToList();
    }

    // ---------------------------------------------------------------- what is being cast

    /// <summary>
    /// The name of what a character is casting. Actions have their own sheet; items, general actions
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

    // The id of a job from its English abbreviation ("BST"), read from the game's own job list (the English table, whatever the language
    // of the game), so new jobs need no update. 0 if none.
    public static uint JobIdByAbbreviation(string abbreviation)
    {
        foreach (var row in Plugin.DataManager.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English))
        {
            if (row.Abbreviation.ToString() == abbreviation)
                return row.RowId;
        }

        return 0;
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
