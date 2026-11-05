using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;
using NahjaUI.Core.Layout;
using NahjaUI.Game;
using ActionSheet = Lumina.Excel.Sheets.Action;

namespace NahjaUI.Modules.Trackers;

/// <summary>
/// Ready-made sets of trackers for a job. They are written by NAME (statuses and abilities) and the ids are looked up in the game's
/// data when the preset is applied, so they are always the ids of the installed game version. What is created is ordinary
/// groups and trackers: they can be edited, moved or deleted like any other.
/// </summary>
internal static class TrackerPresets
{
    /// <summary>The preset of a job, or null when it has none (yet).</summary>
    public static string? NameForJob(uint jobId) => JobSetups.NameOfJob(jobId);

    // Every job with a setup, in the order of the game's job list, and then the PvP ones.
    public static readonly string[] Names = JobSetups.Names.Append("Warrior (PvP)").ToArray();

    /// <summary>Creates the groups and trackers of a preset. Returns a short report to show to the user.</summary>
    public static string Apply(TrackerEngine engine, string name)
    {
        if (name == "Warrior (PvP)")
            return JobSetups.ApplyPvp(engine.Plugin, engine, "Warrior");

        return JobSetups.Names.Contains(name) ? JobSetups.Apply(engine.Plugin, engine, name) : "Unknown preset.";
    }

    // ---------------------------------------------------------------- builder

    internal sealed class Builder
    {
        private readonly TrackerEngine engine;
        private readonly uint jobId;
        private readonly List<uint> jobIds;
        private readonly List<TrackerGroupRuntime> groups = new();

        /// <summary>The trackers are for PvP: they load only there and look up the PvP version of every action and status.</summary>
        public bool Pvp { get; init; }

        private TrackerPvpMode Where => Pvp ? TrackerPvpMode.PvpOnly : TrackerPvpMode.PveOnly;

        public Builder(TrackerEngine engine, uint jobId)
        {
            this.engine = engine;
            this.jobId = jobId;
            jobIds = new List<uint> { jobId };
        }

        /// <summary>For trackers of every job (Sprint): they have no job filter.</summary>
        public Builder(TrackerEngine engine)
        {
            this.engine = engine;
            jobIds = new List<uint>();
        }

        /// <summary>For trackers shared by several jobs (a role): they load on all of them.</summary>
        public Builder(TrackerEngine engine, params uint[] jobs)
        {
            this.engine = engine;
            jobId = jobs[0];
            jobIds = new List<uint>(jobs);
        }

        // ---- The NahjaUI way of laying out a job (see "Job setups" in the project brief): a column of rows, each one at its own height (no anchors between them: the user can move them freely).

        /// <summary>Puts a layout at the top of a column: its top centre at the given height below the middle of the screen.</summary>
        public static void AttachToScreenTop(ElementLayout layout, int topY)
        {
            layout.AttachTo = null;
            layout.ScreenAnchor = AnchorPoint.Center;
            layout.SelfAnchor = AnchorPoint.Top;
            layout.OffsetX = 0;
            layout.OffsetY = topY;
        }

        /// <summary>A row of icons (32x24, rounded, trimmed, growing from the centre). Its place in the column is set afterwards.</summary>
        public TrackerGroupRuntime Row(string name, int spacing, bool sweep)
        {
            var group = engine.AddGroup();
            var definition = group.Definition;
            definition.Name = name;
            definition.Growth = GroupGrowth.FromCenterHorizontal;
            definition.Spacing = spacing;
            definition.IconWidth = 32;
            definition.IconHeight = 24;

            var template = definition.Template ??= new TrackerDefinition();
            template.CornerRadiusPx = 6;
            template.Trim = IconTrim.AllAround;
            template.ShowCooldownSweep = sweep;
            template.Load.JobIds = new List<uint>(jobIds);
            template.Load.Where = Where;

            group.Element.Layout.Width = 32;
            group.Element.Layout.Height = 24;
            groups.Add(group);
            return group;
        }

        // A short enhancement or a ready action: shown only while it is on you, with its time (and stacks) and a glow.
        public void Enhancement(TrackerGroupRuntime group, string statusName, bool stacks = false)
        {
            StatusTracker(group, statusName, glow: true, stacks: stacks);
        }

        // An enfeeblement that is yours on the target and must always be up (a DoT, the Bard's poisons): always shown, with its time, red and dimmed while it
        // is missing. An enfeeblement that is replaced as the job levels up (Aero, Aero II, Dia) is ONE tracker named like the newest; the names go newest first.
        public void Enfeeblement(TrackerGroupRuntime group, params string[] statusNames)
        {
            var tracker = NewTracker(group, statusNames[0]);
            AddUpkeepRules(tracker, TrackerUnit.Target, statusNames);
        }

        // A step of a combo: shown only while the game makes it glow (its button changes into it). No text.
        // A step that the game upgrades by level (Rage of Halone into Royal Authority) is ONE tracker: it follows the lowest ability (the game
        // upgrades the button by itself) and is named like the newest one (shownName).
        public void Step(TrackerGroupRuntime group, string abilityName, string? shownName = null)
        {
            var tracker = NewTracker(group, shownName ?? abilityName);
            var id = FindAction(abilityName, jobId, Pvp);
            if (id == 0)
                Missing.Add($"ability \"{abilityName}\"");

            tracker.Rules.Add(new TrackerRule { Kind = ConditionKind.AbilityActivated, ActionId = id, GlowWhen = EffectWhen.Highlighted, EffectsMigrated = true });
        }

        // The stance of a tank (Iron Will, Defiance, Grit, Royal Guard): shown only while it is off (red and dimmed), no text.
        public void Stance(TrackerGroupRuntime group, string statusName, string? filterId = null)
        {
            var tracker = NewTracker(group, statusName);
            var id = FindStatus(statusName, Pvp);
            if (id == 0)
                Missing.Add($"status \"{statusName}\"");

            tracker.Rules.Add(new TrackerRule
            {
                // Only there while the status is missing (red and dimmed); when it is on, the tracker is not shown at all.
                Kind = ConditionKind.StatusMissing,
                Unit = TrackerUnit.Player,
                StatusId = id,
                FilterId = filterId ?? string.Empty,
                RedWhen = EffectWhen.Always,
                DimWhen = EffectWhen.Always,
                EffectsMigrated = true
            });
        }

        // An enhancement of yours to keep up (Surging Tempest): always shown, with its time; red and dimmed while it is missing.
        // The names that replace each other as the job levels up go newest first, in one tracker.
        public void Upkeep(TrackerGroupRuntime group, params string[] statusNames)
        {
            var tracker = NewTracker(group, statusNames[0]);
            AddUpkeepRules(tracker, TrackerUnit.Player, statusNames);
        }

        // One name: a single rule that is always shown. Several names: one rule for each while it is on (the icon is the one of the status
        // that is there), and a last one that is red while none of them is on, with the icon of the newest.
        private void AddUpkeepRules(TrackerDefinition tracker, TrackerUnit unit, string[] statusNames)
        {
            var ids = new uint[statusNames.Length];
            for (var i = 0; i < statusNames.Length; i++)
            {
                ids[i] = FindStatus(statusNames[i], Pvp);
                if (ids[i] == 0)
                    Missing.Add($"status \"{statusNames[i]}\"");
            }

            TrackerRule Make(ConditionKind kind, uint id)
            {
                return new TrackerRule
                {
                    Kind = kind,
                    Unit = unit,
                    StatusId = id,
                    OnlyMine = unit == TrackerUnit.Target,
                    EffectsMigrated = true
                };
            }

            if (ids.Length == 1)
            {
                var only = Make(ConditionKind.StatusAlways, ids[0]);
                only.RedWhen = EffectWhen.StatusMissing;
                only.DimWhen = EffectWhen.StatusMissing;
                only.Texts.Add(TimeText());
                tracker.Rules.Add(only);
                return;
            }

            foreach (var id in ids)
            {
                var present = Make(ConditionKind.StatusPresent, id);
                present.Texts.Add(TimeText());
                tracker.Rules.Add(present);
            }

            var missing = Make(ConditionKind.StatusMissing, ids[0]);
            missing.RedWhen = EffectWhen.StatusMissing;
            missing.DimWhen = EffectWhen.StatusMissing;
            tracker.Rules.Add(missing);
        }

        // An ability: while its effect is on you, the time of the effect; otherwise its cooldown (dimmed while it cannot be used).
        // Alternative names (an ability that is replaced at a higher level) go newest first: the engine skips the ones not learned.
        public void Cd(TrackerGroupRuntime group, string trackerName, string[] abilities, string[]? effects = null, bool charges = false, bool glow = true, bool effectStacks = false)
        {
            var tracker = NewTracker(group, trackerName);

            foreach (var effect in effects ?? Array.Empty<string>())
            {
                var effectRule = StatusRule(effect, glow: false);
                effectRule.Texts.Add(TimeText());

                if (effectStacks)
                    effectRule.Texts.Add(StacksText());

                tracker.Rules.Add(effectRule);
            }

            // The abilities of the list are versions of the same one (Berserk and Inner Release, Sentinel and Guardian): one tracker, with one rule
            // for each, the newest first. An ability that is not learned yet is skipped, so the tracker follows the right version at every level
            // (the versions do not always share their cooldown, so following only the oldest one is wrong).
            // The tracker keeps the name it was given (the newest version).
            var chosen = NewestFirst(abilities);

            // An ability that the game says stores several charges shows them even when the preset did not say so.
            if (!charges)
                charges = chosen.Any(name => HasCharges(FindAction(name, jobId, Pvp)));

            foreach (var abilityName in chosen)
            {
                var rule = AbilityRule(abilityName);
                rule.DimWhen = charges ? EffectWhen.Unavailable : EffectWhen.Recharging;
                // By default every ability glows when the game makes it glow (a ready action, a combo that opens it).
                rule.GlowWhen = glow ? EffectWhen.Highlighted : EffectWhen.Never;
                rule.Texts.Add(TimeText());

                if (charges)
                    rule.Texts.Add(StacksText());

                tracker.Rules.Add(rule);
            }
        }

        // A group that was created and turned out to be empty (and was deleted) is not styled at the end.
        public void Forget(TrackerGroupRuntime group) => groups.Remove(group);

        public int Count { get; private set; }

        /// <summary>The abilities (action ids) that have a tracker made by this builder.</summary>
        public HashSet<uint> TrackedActions { get; } = new();

        /// <summary>Adds the result of another builder (trackers created and names not found) to this report.</summary>
        public void Add(int count, IEnumerable<string> missing)
        {
            Count += count;
            Missing.AddRange(missing);
        }
        public List<string> Missing { get; } = new();

        // An enhancement or a ready action: shown only while the status is on you. Text: its time (and, optionally, its stacks).
        public void StatusTracker(TrackerGroupRuntime group, string statusName, bool glow = false, bool stacks = false)
        {
            var tracker = NewTracker(group, statusName);
            var rule = StatusRule(statusName, glow);
            rule.Texts.Add(TimeText());

            if (stacks)
                rule.Texts.Add(StacksText());

            tracker.Rules.Add(rule);
        }

        // The look of the groups is copied into every tracker once they all exist.
        public void Finish()
        {
            foreach (var group in groups)
                engine.ApplyGroupStyle(group);
        }

        private TrackerDefinition NewTracker(TrackerGroupRuntime group, string name)
        {
            var runtime = engine.AddNew(group);
            var definition = runtime.Definition;
            definition.Name = name;
            definition.Rules.Clear();
            definition.Texts.Clear();
            definition.CornerRadiusPx = 6;
            definition.Trim = IconTrim.AllAround;
            definition.Load.JobIds = new List<uint>(jobIds);
            definition.Load.Where = Where;
            Count++;
            return definition;
        }

        private TrackerRule StatusRule(string statusName, bool glow)
        {
            var id = FindStatus(statusName, Pvp);
            if (id == 0)
                Missing.Add($"status \"{statusName}\"");

            return new TrackerRule
            {
                Kind = ConditionKind.StatusPresent,
                Unit = TrackerUnit.Player,
                StatusId = id,
                GlowWhen = glow ? EffectWhen.Always : EffectWhen.Never,
                EffectsMigrated = true
            };
        }

        private TrackerRule AbilityRule(string abilityName)
        {
            var id = FindAction(abilityName, jobId, Pvp);
            if (id == 0)
                Missing.Add($"ability \"{abilityName}\"");
            else
                TrackedActions.Add(id);

            return new TrackerRule
            {
                Kind = ConditionKind.AbilityAlways,
                ActionId = id,
                DimWhen = EffectWhen.Unavailable,
                EffectsMigrated = true
            };
        }

        // 12 px, centred, no offset: the time (status time or cooldown of the active trigger).
        private static TrackerTextDefinition TimeText() => new()
        {
            Size = 12,
            Anchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
            Sources = { new TrackerTextSource { Source = TextSource.OwnTime } }
        };

        // 9 px, bottom right, no offset: the stacks of the status or the charges of the ability.
        private static TrackerTextDefinition StacksText() => new()
        {
            Size = 9,
            Anchor = AnchorPoint.BottomRight,
            OffsetX = 0,
            OffsetY = 0,
            Sources = { new TrackerTextSource { Source = TextSource.OwnStacks } }
        };
    }

    // ---------------------------------------------------------------- lookup by name

    // The abilities from the one learned at the highest level to the lowest (names that do not exist keep their place at the end, so they are reported).
    private static string[] NewestFirst(string[] abilities)
    {
        return abilities
            .OrderByDescending(name => ActionLevel(FindAction(name, 0)))
            .ToArray();
    }

    private static bool HasCharges(uint actionId)
    {
        return actionId != 0 && FFXIVClientStructs.FFXIV.Client.Game.ActionManager.GetMaxCharges(actionId, 100) > 1;
    }

    private static int ActionLevel(uint actionId)
    {
        return actionId != 0 && Plugin.DataManager.GetExcelSheet<ActionSheet>().TryGetRow(actionId, out var row) ? row.ClassJobLevel : 0;
    }

    // The status with exactly this name. Several statuses can share a name (PvP, old versions): the lowest id is taken.
    // For PvP the highest id is taken instead: the PvP version of a status is made after the PvE one of the same name.
    private static uint FindStatus(string name, bool pvp = false)
    {
        uint found = 0;
        // The names of the presets are English: they are looked up in the English table whatever the language of the game, and the tracker keeps the id.
        foreach (var row in Plugin.DataManager.GetExcelSheet<Status>(Dalamud.Game.ClientLanguage.English))
        {
            if (!string.Equals(row.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!pvp)
                return row.RowId;

            found = row.RowId;
        }

        return found;
    }

    // The action with exactly this name, not PvP, preferring the one of the job.
    // With pvp, the PvP action (of the job, or a common/role one) instead.
    private static uint FindAction(string name, uint jobId, bool pvp = false)
    {
        uint fallback = 0;
        uint anyMatch = 0;

        foreach (var row in Plugin.DataManager.GetExcelSheet<ActionSheet>(Dalamud.Game.ClientLanguage.English))
        {
            if (pvp)
            {
                if (!row.IsPvP || !string.Equals(row.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (row.ClassJob.RowId == jobId)
                    return row.RowId;

                if (fallback == 0)
                    fallback = row.RowId;

                continue;
            }

            if (row.IsPvP || !string.Equals(row.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (row.ClassJob.RowId == jobId)
                return row.RowId;

            if (fallback == 0 && row.IsPlayerAction)
                fallback = row.RowId;

            // Some follow-up actions (Blade of Honor) are not flagged as player actions and have no job: the last resort.
            if (anyMatch == 0)
                anyMatch = row.RowId;
        }

        return fallback != 0 ? fallback : anyMatch;
    }
}
