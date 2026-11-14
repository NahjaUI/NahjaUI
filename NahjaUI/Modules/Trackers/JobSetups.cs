using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using NahjaUI.Core.Layout;
using NahjaUI.Game;

namespace NahjaUI.Modules.Trackers;

/// <summary>
/// The NahjaUI way of laying out a job, one button per job (Trackers > Presets). One column of rows centred under the character:
/// the top row (normally Rotation) sits at a fixed height and every other row hangs 6 px under the one above it, so the column
/// grows downwards. Tanks: Rotation, gauge, CDs, Defensive CDs, Tank Role, progress bar. Jobs that cast a lot (healers, casters):
/// Rotation, gauge, MP bar, progress bar, CDs, Defensive CDs. Rows are 32x24 icons that grow from the centre; bars are 240 wide.
/// The groups are ordinary groups and trackers: they can be edited, moved or deleted afterwards. Names are looked up in the game's data,
/// so a name that does not exist is reported and its tracker does nothing.
/// </summary>
internal static class JobSetups
{
    // The abilities every job of a role shares are one row for the whole role ("Tank Role", "Melee Role"...).
    private sealed record Role(string Name, uint[] Jobs, Action<TrackerPresets.Builder, TrackerGroupRuntime> Fill);

    private static readonly Role[] Roles =
    {
        new("Tank Role", new uint[] { 19, 21, 32, 37 }, TankRole),
        new("Healer Role", new uint[] { 24, 28, 33, 40 }, HealerRole),
        new("Melee Role", new uint[] { 20, 22, 30, 34, 39, 41 }, MeleeRole),
        new("Physical Ranged Role", new uint[] { 23, 31, 38 }, PhysicalRangedRole),
        new("Magical Ranged Role", new uint[] { 25, 27, 35, 42 }, MagicalRangedRole),
    };

    // The height of the top of the column, from the middle of the screen (the top of the Warrior's Rotation row).
    private const int TopY = 180;

    // The space between two bars (gauges, MP bar and progress bar) one under the other. Everything else in the column is 6 px apart.
    private const int BarGap = 1;

    // A movement ability without damage: it goes next to Sprint in the group "General". Charges are read from the game too.
    private sealed record Move(string Name, bool Charges = false, string? Effect = null);

    private sealed record Job(
        uint Id,
        string Abbreviation,
        Func<Plugin, LayoutElement[]> Gauge,
        Action<TrackerPresets.Builder, TrackerGroupRuntime> Rotation,
        Action<TrackerPresets.Builder, TrackerGroupRuntime> Cooldowns,
        Action<TrackerPresets.Builder, TrackerGroupRuntime> Defensives,
        bool MpBar = false,
        Move[]? Moves = null,
        string? Stance = null,
        bool Spellbook = false);

    // The gauge elements go from the top one to the bottom one.
    private static readonly Dictionary<string, Job> Jobs = new()
    {
        ["Paladin"] = new Job(19, "PLD", p => new[] { p.JobGaugeSystem.PaladinElement }, PaladinRotation, PaladinCooldowns, PaladinDefensives, Stance: "Iron Will"),
        ["Warrior"] = new Job(21, "WAR", p => new[] { p.JobGaugeSystem.WarriorElement }, WarriorRotation, WarriorCooldowns, WarriorDefensives, Stance: "Defiance"),
        ["Dark Knight"] = new Job(32, "DRK", p => new[] { p.JobGaugeSystem.DarkBloodElement, p.JobGaugeSystem.DarksideElement }, DarkKnightRotation, DarkKnightCooldowns, DarkKnightDefensives, MpBar: true, Moves: new[] { new Move("Shadowstride", true) }, Stance: "Grit"),
        ["Gunbreaker"] = new Job(37, "GNB", p => new[] { p.JobGaugeSystem.GunbreakerElement }, GunbreakerRotation, GunbreakerCooldowns, GunbreakerDefensives, Moves: new[] { new Move("Trajectory", true) }, Stance: "Royal Guard"),
        ["White Mage"] = new Job(24, "WHM", p => new[] { p.JobGaugeSystem.LilyElement, p.JobGaugeSystem.BloodLilyElement }, WhiteMageRotation, WhiteMageCooldowns, WhiteMageDefensives, Moves: new[] { new Move("Aetherial Shift") }),
        ["Scholar"] = new Job(28, "SCH", p => new[] { p.JobGaugeSystem.AetherflowElement, p.JobGaugeSystem.FaerieElement }, ScholarRotation, ScholarCooldowns, ScholarDefensives),
        ["Astrologian"] = new Job(33, "AST", p => new[] { p.JobGaugeSystem.CardsElement, p.JobGaugeSystem.CrownElement }, AstrologianRotation, AstrologianCooldowns, AstrologianDefensives),
        ["Sage"] = new Job(40, "SGE", p => new[] { p.JobGaugeSystem.AddersgallElement, p.JobGaugeSystem.AdderstingElement }, SageRotation, SageCooldowns, SageDefensives, Moves: new[] { new Move("Icarus") }),
        ["Monk"] = new Job(20, "MNK", p => p.JobGaugeSystem.ElementsOf("MNK"), MonkRotation, MonkCooldowns, MonkDefensives, Moves: new[] { new Move("Thunderclap", true) }),
        ["Dragoon"] = new Job(22, "DRG", p => p.JobGaugeSystem.ElementsOf("DRG"), DragoonRotation, DragoonCooldowns, NoAbilities, Moves: new[] { new Move("Winged Glide", true), new Move("Elusive Jump") }),
        ["Ninja"] = new Job(30, "NIN", p => p.JobGaugeSystem.ElementsOf("NIN"), NinjaRotation, NinjaCooldowns, NinjaDefensives, Moves: new[] { new Move("Shukuchi", true) }),
        ["Samurai"] = new Job(34, "SAM", p => p.JobGaugeSystem.ElementsOf("SAM"), SamuraiRotation, SamuraiCooldowns, SamuraiDefensives),
        ["Reaper"] = new Job(39, "RPR", p => p.JobGaugeSystem.ElementsOf("RPR"), ReaperRotation, ReaperCooldowns, ReaperDefensives, Moves: new[] { new Move("Hell's Ingress"), new Move("Hell's Egress") }),
        ["Viper"] = new Job(41, "VPR", p => p.JobGaugeSystem.ElementsOf("VPR"), ViperRotation, ViperCooldowns, NoAbilities, Moves: new[] { new Move("Slither", true) }),
        ["Bard"] = new Job(23, "BRD", p => p.JobGaugeSystem.ElementsOf("BRD"), BardRotation, BardCooldowns, BardDefensives, Moves: new[] { new Move("Peloton", Effect: "Peloton"), new Move("Repelling Shot") }),
        ["Machinist"] = new Job(31, "MCH", p => p.JobGaugeSystem.ElementsOf("MCH"), MachinistRotation, MachinistCooldowns, MachinistDefensives, Moves: new[] { new Move("Peloton", Effect: "Peloton") }),
        ["Dancer"] = new Job(38, "DNC", p => p.JobGaugeSystem.ElementsOf("DNC"), DancerRotation, DancerCooldowns, DancerDefensives, Moves: new[] { new Move("Peloton", Effect: "Peloton"), new Move("En Avant", true) }, Stance: "Closed Position"),
        ["Black Mage"] = new Job(25, "BLM", p => p.JobGaugeSystem.ElementsOf("BLM"), BlackMageRotation, BlackMageCooldowns, BlackMageDefensives, Moves: new[] { new Move("Aetherial Manipulation") }),
        ["Summoner"] = new Job(27, "SMN", p => p.JobGaugeSystem.ElementsOf("SMN"), SummonerRotation, SummonerCooldowns, SummonerDefensives),
        ["Red Mage"] = new Job(35, "RDM", p => new[] { p.JobGaugeSystem.RedWhiteElement, p.JobGaugeSystem.RedBlackElement, p.JobGaugeSystem.RedStackElement }, RedMageRotation, RedMageCooldowns, RedMageDefensives),
        ["Pictomancer"] = new Job(42, "PCT", p => p.JobGaugeSystem.ElementsOf("PCT"), PictomancerRotation, PictomancerCooldowns, PictomancerDefensives, Moves: new[] { new Move("Smudge") }),
        // The Blue Mage has no gauge and a spellbook of about 120 spells: only the ones chosen below get a tracker (no safety net).
        ["Blue Mage"] = new Job(36, "BLU", p => Array.Empty<LayoutElement>(), BlueMageRotation, BlueMageCooldowns, BlueMageDefensives, MpBar: true, Spellbook: true),
    };

    // The jobs without defensive abilities have no defensives row.
    private static void NoAbilities(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
    }

    public static string[] Names => Jobs.Keys.ToArray();

    public static string? NameOfJob(uint jobId) => Jobs.FirstOrDefault(pair => pair.Value.Id == jobId).Key;

    // One place of the column: an element, whether it has a position for each job (shared elements) and the size to give it (0 = keep).
    private sealed record Slot(LayoutElement Element, bool PerJob, int Width = 0, int Height = 0);

    public static string Apply(Plugin plugin, TrackerEngine engine, string jobName)
    {
        if (!Jobs.TryGetValue(jobName, out var job))
            return "Unknown job.";

        var rotationName = $"{job.Abbreviation} Rotation";
        var cooldownsName = $"{job.Abbreviation} CDs";
        var defensivesName = $"{job.Abbreviation} Defensive CDs";

        // Groups that already exist are only put in their place in the column (their trackers are not touched).
        var relayout = engine.Groups.Any(g => g.Definition.Name == rotationName || g.Definition.Name == cooldownsName || g.Definition.Name == defensivesName);

        var builder = new TrackerPresets.Builder(engine, job.Id);
        var added = new List<string>();

        TrackerGroupRuntime? Find(string name) => engine.Groups.FirstOrDefault(g => g.Definition.Name == name);

        // The stance of a tank (its enmity tracker) goes in "General", first of the job: it only says whether the stance is on. It is also added
        // when the job was set up before, if it is missing.
        if (job.Stance is not null)
        {
            var general = Find("General") ?? CreateGeneral(plugin, engine, builder);
            var exists = engine.MembersOf(general).Any(a => a.Definition.Name == job.Stance && a.Definition.Load.JobIds.Contains(job.Id));
            if (!exists)
            {
                builder.Stance(general, job.Stance);
                PutInJobOrder(engine, general, job.Id);
            }
        }

        // Jog (every job) goes in "General" right after Sprint. It is also added when "General" was made before it existed.
        {
            var general = Find("General") ?? CreateGeneral(plugin, engine, builder);
            if (!engine.MembersOf(general).Any(a => a.Definition.Name == "Jog"))
            {
                var all = new TrackerPresets.Builder(engine);
                all.PlainStatus(general, "Jog");
                all.Finish();
                builder.Add(all.Count, all.Missing);

                var jog = engine.Trackers.LastOrDefault(a => a.Definition.GroupId == general.Definition.Id);
                var sprint = engine.MembersOf(general).FirstOrDefault(a => a.Definition.Name == "Sprint");
                if (jog is not null && sprint is not null && jog != sprint)
                    engine.MoveTrackerIntoGroup(jog, general, sprint, true);
            }
        }

        // The Peloton enhancement (another player's, or your own) is shown in "General" for every job that has no Peloton of its own: the ranged
        // physical jobs track the ability itself (with its effect) next to Sprint.
        if (job.Moves is null || !job.Moves.Any(move => move.Name == "Peloton"))
        {
            var general = Find("General") ?? CreateGeneral(plugin, engine, builder);
            var exists = engine.MembersOf(general).Any(a => a.Definition.Name == "Peloton" && a.Definition.Load.JobIds.Contains(job.Id));
            if (!exists)
            {
                builder.StatusTracker(general, "Peloton", glow: false); // Peloton never glows
                PutInJobOrder(engine, general, job.Id);
            }
        }

        TrackerGroupRuntime? rotation, cooldowns, defensives;
        if (relayout)
        {
            rotation = Find(rotationName);
            cooldowns = Find(cooldownsName);
            defensives = Find(defensivesName);
        }
        else
        {
            rotation = builder.Row(rotationName, 6, false);
            job.Rotation(builder, rotation);

            cooldowns = builder.Row(cooldownsName, 3, true);
            job.Cooldowns(builder, cooldowns);

            defensives = builder.Row(defensivesName, 3, true);
            job.Defensives(builder, defensives);

            // A job without defensive abilities (Dragoon, Viper) has no such row.
            if (!engine.MembersOf(defensives).Any())
            {
                engine.RemoveGroup(defensives);
                builder.Forget(defensives);
                defensives = null;
            }

            // The movement abilities without damage (Shadowstride, Aetherial Shift) go next to Sprint in the group "General"
            // (the ones that hit stay in the CDs). They are tracked here, before the safety net below, so they are not added twice.
            if (job.Moves is not null)
            {
                var general = Find("General") ?? CreateGeneral(plugin, engine, builder);
                var known = GameNames.AbilitiesOfJob(job.Id, 100);

                foreach (var move in job.Moves)
                {
                    var stores = known.Any(info => info.Name == move.Name && (info.MaxCharges > 1 || ActionManager.GetMaxCharges(info.Id, 100) > 1));
                    builder.Cd(general, move.Name, new[] { move.Name }, move.Effect is null ? null : new[] { move.Effect }, charges: move.Charges || stores, glow: move.Name != "Peloton");
                    PutInJobOrder(engine, general, job.Id);
                }
            }

            // Every ability of the job that has a cooldown must have a tracker (so the hotbars can be hidden): what the lists above
            // missed is read from the game's data and added to the CDs row.
            // Abilities that share their cooldown (use one and the other recharges too) count as one: a single tracker is enough.
            var trackedGroups = new HashSet<int>(builder.TrackedActions.Select(RecastGroup).Where(group => group >= 0));

            foreach (var info in job.Spellbook ? Array.Empty<ActionInfo>() : GameNames.AbilitiesOfJob(job.Id, 100))
            {
                var group = RecastGroup(info.Id);
                if (info.Id == GameNames.SprintId || builder.TrackedActions.Contains(info.Id) || (group >= 0 && trackedGroups.Contains(group)))
                    continue;

                builder.Cd(cooldowns, info.Name, new[] { info.Name }, charges: Math.Max(info.MaxCharges, (int)ActionManager.GetMaxCharges(info.Id, 100)) > 1);
                added.Add(info.Name);

                if (group >= 0)
                    trackedGroups.Add(group);
            }
        }

        // The abilities every job of a role shares are one row for the whole role.
        TrackerGroupRuntime? roleRow = null;
        var roleIsNew = false;
        var role = Roles.FirstOrDefault(r => r.Jobs.Contains(job.Id));
        if (role is not null)
        {
            roleRow = Find(role.Name);
            if (roleRow is null)
            {
                roleIsNew = true;
                var shared = new TrackerPresets.Builder(engine, role.Jobs);
                roleRow = shared.Row(role.Name, 3, false);
                role.Fill(shared, roleRow);
                shared.Finish();
                builder.Add(shared.Count, shared.Missing);
            }
        }

        // The list of trackers keeps the order of the game's job list: tanks, healers, melee, physical ranged, magical ranged, each job with its
        // Rotation, CDs and Defensive CDs, the row of the role after its last job, and "General" (Sprint, for every job) at the end.
        // The user can move them afterwards; this only decides where a preset puts what it creates.
        if (!relayout)
        {
            PlaceInOrder(engine, rotation);
            PlaceInOrder(engine, cooldowns);
            PlaceInOrder(engine, defensives);

            if (roleIsNew)
                PlaceInOrder(engine, roleRow);

            PlaceInOrder(engine, Find("General"));
        }

        var progressBar = new Slot(plugin.ProgressBarSystem.PlayerElement, true, 240, 18);

        // From the top of the column down.
        var column = new List<Slot>();
        if (rotation is not null)
            column.Add(new Slot(rotation.Element, false));

        column.AddRange(job.Gauge(plugin).Select(bar => new Slot(bar, false, 240, 6)));

        void AddRows()
        {
            if (cooldowns is not null)
                column.Add(new Slot(cooldowns.Element, false));

            if (defensives is not null)
                column.Add(new Slot(defensives.Element, false));
        }

        // Dark Knight is the one tank that also needs its MP (Edge of Shadow, The Blackest Night).
        if (job.MpBar)
            plugin.Configuration.HudBars.Player.SetMpOn(job.Id, true);

        var player = plugin.Configuration.HudBars.Player;
        var showsMp = player.IsMpOn(job.Id);
        if (showsMp)
            column.Add(new Slot(plugin.HudBarSystem.MpElement, true, 240, 6));

        // The global cooldown bar sits between the MP bar (or the gauges) and the progress bar.
        column.Add(new Slot(plugin.ProgressBarSystem.GcdElement, true, 240, 6));
        column.Add(progressBar);
        AddRows();

        if (roleRow is not null)
            column.Add(new Slot(roleRow.Element, true));

        // Every element of the column has its own height measured from the middle of the screen (no anchor to the one above, so moving
        // one of them never drags the others by surprise): 6 px between rows, 1 px between bars (gauges, MP bar, progress bar).
        var y = TopY;
        var aboveIsBar = false;
        var first = true;
        foreach (var slot in column)
        {
            var isBar = slot.Width > 0;
            if (!first)
                y += aboveIsBar && isBar ? BarGap : 6;

            var top = y;

            void Place(ElementLayout layout)
            {
                TrackerPresets.Builder.AttachToScreenTop(layout, top);

                if (slot.Width > 0)
                {
                    layout.Width = slot.Width;
                    layout.Height = slot.Height;
                }
            }

            if (slot.PerJob)
                PlaceForJob(plugin, slot.Element, job.Id, Place);
            else
                Place(slot.Element.Layout);

            y += isBar ? slot.Height : slot.Element.Layout.Height;
            aboveIsBar = isBar;
            first = false;
        }

        builder.Finish();
        plugin.RequestSave();

        var report = relayout
            ? $"{jobName} setup applied. The progress bar{(showsMp ? " and the MP bar were" : " was")} moved too."
            : $"{jobName} setup added: {builder.Count} trackers. The progress bar{(showsMp ? " and the MP bar were" : " was")} moved too.";


        if (added.Count > 0)
            report += $" Also added to the CDs row because the game says they have a cooldown: {string.Join(", ", added)}.";

        if (builder.Missing.Count > 0)
            report += $" Not found in the game data, so those triggers do nothing: {string.Join(", ", builder.Missing.Distinct())}.";

        return report;
    }

    // The jobs in the order of the game's job list: tanks, healers, melee, physical ranged, magical ranged. Used to order the movement abilities
    // of the group "General" (Sprint, which every job has, goes first).
    private static readonly uint[] JobOrder =
    {
        19, 21, 32, 37, 24, 28, 33, 40, 20, 22, 30, 34, 39, 41, 23, 31, 38, 25, 27, 35, 42, 36
    };

    private static int OrderOf(TrackerDefinition tracker)
    {
        if (tracker.Load.JobIds.Count == 0)
            return -1;

        var index = Array.IndexOf(JobOrder, tracker.Load.JobIds[0]);
        return index < 0 ? JobOrder.Length : index;
    }

    // The tracker just added to the group (the last of it) goes after the last tracker of a job that comes before its own, or of every job.
    private static void PutInJobOrder(TrackerEngine engine, TrackerGroupRuntime group, uint jobId)
    {
        var members = engine.Trackers.Where(a => a.Definition.GroupId == group.Definition.Id).ToList();
        if (members.Count < 2)
            return;

        var added = members[^1];
        var order = Array.IndexOf(JobOrder, jobId);
        var neighbour = members.Take(members.Count - 1).LastOrDefault(a => OrderOf(a.Definition) <= order);
        if (neighbour is null)
            return;

        engine.MoveTrackerIntoGroup(added, group, neighbour, true);
    }

    // Where a group goes in the list of trackers: a number that follows the order of the game's job list. -1 for groups that are not
    // from a preset (the user's own groups stay where they are).
    private static int RankOf(string name)
    {
        if (name == "General")
            return 100000;

        var role = Roles.FirstOrDefault(r => r.Name == name);
        if (role is not null)
            return role.Jobs.Max(id => Array.IndexOf(JobOrder, id)) * 10 + 5;

        foreach (var (jobName, job) in Jobs)
        {
            var order = Array.IndexOf(JobOrder, job.Id) * 10;
            if (name == $"{job.Abbreviation} Rotation")
                return order;

            if (name == $"{job.Abbreviation} CDs")
                return order + 1;

            if (name == $"{job.Abbreviation} Defensive CDs")
                return order + 2;
        }

        return -1;
    }

    // Puts a group in the top-level list after the last group that comes before it in the job order (or before the first that comes after).
    private static void PlaceInOrder(TrackerEngine engine, TrackerGroupRuntime? group)
    {
        if (group is null)
            return;

        var rank = RankOf(group.Definition.Name);
        if (rank < 0)
            return;

        var order = engine.Plugin.Configuration.RootOrder;
        var key = TrackerEngine.GroupKey(group.Definition);
        order.Remove(key);

        var after = -1;
        var before = -1;
        for (var i = 0; i < order.Count; i++)
        {
            if (!order[i].StartsWith("group:", StringComparison.Ordinal))
                continue;

            var other = engine.FindGroup(order[i]["group:".Length..]);
            var otherRank = other is null ? -1 : RankOf(other.Definition.Name);
            if (otherRank < 0)
                continue;

            if (otherRank <= rank)
                after = i + 1;
            else if (before < 0)
                before = i;
        }

        if (after >= 0)
            order.Insert(after, key);
        else if (before >= 0)
            order.Insert(before, key);
        else
            order.Add(key);

        engine.Plugin.RequestSave();
    }

    // The group "General": Sprint and Jog (every job) and the movement of the jobs that have one without damage. Left of the player frame, growing to the left.
    // It is placed with its own offsets, not attached to the player frame.
    private static TrackerGroupRuntime CreateGeneral(Plugin plugin, TrackerEngine engine, TrackerPresets.Builder report)
    {
        var all = new TrackerPresets.Builder(engine);
        var group = all.Row("General", 3, true);
        group.Definition.Growth = GroupGrowth.ToLeft;

        var frame = plugin.HudBarSystem.PlayerElement.Layout;
        var centered = frame.AttachTo is null && frame.ScreenAnchor == AnchorPoint.Center && frame.SelfAnchor == AnchorPoint.Center;

        var layout = group.Element.Layout;
        layout.AttachTo = null;
        layout.ScreenAnchor = AnchorPoint.Center;
        layout.SelfAnchor = AnchorPoint.Right;
        layout.OffsetX = (centered ? frame.OffsetX - frame.Width / 2 : -470) - 6;
        layout.OffsetY = centered ? frame.OffsetY : 190;

        all.Cd(group, "Sprint", new[] { "Sprint" }, new[] { "Sprint" });
        all.PlainStatus(group, "Jog");
        all.Finish();
        report.Add(all.Count, all.Missing);
        return group;
    }

    // The cooldown group the game uses for an ability: abilities of the same group share their cooldown. -1 when unknown.
    private static unsafe int RecastGroup(uint actionId)
    {
        var manager = ActionManager.Instance();
        return manager == null ? -1 : manager->GetRecastGroup((int)ActionType.Action, actionId);
    }

    // Deletes a group with its trackers (to rebuild a job that was set up by hand in an older style). True when it existed.
    private static bool RemoveOldGroup(TrackerEngine engine, string name)
    {
        var group = engine.Groups.FirstOrDefault(g => g.Definition.Name == name);
        if (group is null)
            return false;

        foreach (var member in engine.MembersOf(group).ToList())
            engine.Remove(member);

        engine.RemoveGroup(group);
        return true;
    }

    // ---------------------------------------------------------------- PvP

    private const string WarriorPvpRotation = "WAR PvP Rotation";
    private const string WarriorPvpCooldowns = "WAR PvP CDs";
    private const string WarriorPvpRole = "WAR PvP Role";

    /// <summary>The same column as the PvE setups, for PvP: Rotation, CDs, Role/common actions. The trackers load only in PvP.</summary>
    public static string ApplyPvp(Plugin plugin, TrackerEngine engine, string jobName)
    {
        if (jobName != "Warrior")
            return "Unknown job.";

        const uint warrior = 21;
        var replaced = new[] { WarriorPvpRotation, WarriorPvpCooldowns, WarriorPvpRole }.Count(name => RemoveOldGroup(engine, name));

        var b = new TrackerPresets.Builder(engine, warrior) { Pvp = true };

        // Rotation: the steps of the combo while the game highlights them, and what unlocks the follow-up actions.
        var rotation = b.Row(WarriorPvpRotation, 6, false);
        b.Step(rotation, "Maim");
        b.Step(rotation, "Storm's Path");
        b.Enhancement(rotation, "Primal Ruination Ready");
        b.Enhancement(rotation, "Inner Chaos Ready");
        b.Enhancement(rotation, "Chaotic Cyclone Ready");
        b.Enhancement(rotation, "Wrathful");
        b.Enhancement(rotation, "Burgeoning Fury", stacks: true);
        b.Enhancement(rotation, "Inner Release");

        // Cooldowns: effect time while active, otherwise the recharge.
        var cooldowns = b.Row(WarriorPvpCooldowns, 3, true);
        b.Cd(cooldowns, "Primal Rend", new[] { "Primal Rend" });
        b.Cd(cooldowns, "Onslaught", new[] { "Onslaught" });
        b.Cd(cooldowns, "Orogeny", new[] { "Orogeny" });
        b.Cd(cooldowns, "Blota", new[] { "Blota" });
        b.Cd(cooldowns, "Bloodwhetting", new[] { "Bloodwhetting" }, new[] { "Bloodwhetting" });

        // Role actions and the common actions of PvP.
        var role = b.Row(WarriorPvpRole, 3, true);
        b.Cd(role, "Rampart", new[] { "Rampart" }, new[] { "Rampart" });
        b.Cd(role, "Rampage", new[] { "Rampage" });
        b.Cd(role, "Full Swing", new[] { "Full Swing" });
        b.Cd(role, "Guard", new[] { "Guard" }, new[] { "Guard" });
        b.Cd(role, "Purify", new[] { "Purify" });
        b.Cd(role, "Recuperate", new[] { "Recuperate" });
        b.Cd(role, "Standard-issue Elixir", new[] { "Standard-issue Elixir" });

        b.Finish();

        // The column: Rotation at the same height as in PvE, the rest under it (each at its own height).
        var rowTop = TopY;
        foreach (var row in new[] { rotation, cooldowns, role })
        {
            TrackerPresets.Builder.AttachToScreenTop(row.Element.Layout, rowTop);
            rowTop += row.Element.Layout.Height + 6;
        }

        plugin.RequestSave();

        var report = $"Warrior PvP setup added: {b.Count} trackers in 3 rows (they only load in PvP).";
        if (replaced > 0)
            report += $" The old {replaced} PvP group(s) were replaced.";

        if (b.Missing.Count > 0)
            report += $" Not found in the game data, so those triggers do nothing: {string.Join(", ", b.Missing)}.";

        return report;
    }

    // An element shared by all jobs (the progress bar, the MP bar, the Tank Role row) gets its own position for this job.
    private static void PlaceForJob(Plugin plugin, LayoutElement element, uint jobId, Action<ElementLayout> place)
    {
        if (!plugin.Layout.IsPerJob(element.Id))
            plugin.Layout.SetPerJob(element, true, 0);

        var jobs = plugin.Configuration.JobLayouts[element.Id];
        if (!jobs.TryGetValue(jobId, out var layout))
        {
            layout = (jobs.TryGetValue(0, out var baseLayout) ? baseLayout : element.Layout).Clone();
            jobs[jobId] = layout;
        }

        place(layout);

        // If this is the job being played, the element shows its position now (and would save it back over ours).
        if (plugin.GameData.Current.Player?.JobId == jobId)
            element.Layout.CopyPositionFrom(layout);
    }

    // ---------------------------------------------------------------- tanks

    private static void TankRole(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Shirk (the odd one), then the defensive ones, then the crowd control, each from the longest cooldown to the shortest.
        b.Cd(row, "Shirk", new[] { "Shirk" }, glow: false);
        b.Cd(row, "Arm's Length", new[] { "Arm's Length" }, new[] { "Arm's Length" }, glow: false);
        b.Cd(row, "Reprisal", new[] { "Reprisal" }, glow: false);
        b.Cd(row, "Rampart", new[] { "Rampart" }, new[] { "Rampart" }, glow: false);
        b.Cd(row, "Provoke", new[] { "Provoke" }, glow: false);
        b.Cd(row, "Interject", new[] { "Interject" }, glow: false);
        b.Cd(row, "Low Blow", new[] { "Low Blow" }, glow: false);
    }

    private static void HealerRole(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The order chosen by the owner: Surecast, Rescue, Swiftcast, Lucid Dreaming.
        b.Cd(row, "Surecast", new[] { "Surecast" }, new[] { "Surecast" });
        b.Cd(row, "Rescue", new[] { "Rescue" });
        b.Cd(row, "Swiftcast", new[] { "Swiftcast" }, new[] { "Swiftcast" });
        b.Cd(row, "Lucid Dreaming", new[] { "Lucid Dreaming" }, new[] { "Lucid Dreaming" });
    }

    private static void MeleeRole(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // True North (the odd one, for the positionals), then the defensive ones, then the crowd control, each from the longest cooldown to the shortest.
        b.Cd(row, "True North", new[] { "True North" }, new[] { "True North" }, charges: true);
        b.Cd(row, "Arm's Length", new[] { "Arm's Length" }, new[] { "Arm's Length" });
        b.Cd(row, "Second Wind", new[] { "Second Wind" });
        b.Cd(row, "Feint", new[] { "Feint" });
        b.Cd(row, "Bloodbath", new[] { "Bloodbath" }, new[] { "Bloodbath" });
        b.Cd(row, "Leg Sweep", new[] { "Leg Sweep" });
    }

    private static void PhysicalRangedRole(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The defensive one, then the crowd control (Peloton is in "General", next to Sprint).
        b.Cd(row, "Second Wind", new[] { "Second Wind" });
        b.Cd(row, "Foot Graze", new[] { "Foot Graze" });
        b.Cd(row, "Leg Graze", new[] { "Leg Graze" });
        b.Cd(row, "Head Graze", new[] { "Head Graze" });
    }

    private static void MagicalRangedRole(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The rest from the longest cooldown to the shortest. Addle is an enfeeblement on the enemy: only its cooldown is shown.
        b.Cd(row, "Surecast", new[] { "Surecast" }, new[] { "Surecast" });
        b.Cd(row, "Addle", new[] { "Addle" });
        b.Cd(row, "Lucid Dreaming", new[] { "Lucid Dreaming" }, new[] { "Lucid Dreaming" });
        b.Cd(row, "Swiftcast", new[] { "Swiftcast" }, new[] { "Swiftcast" });
    }

    // ---- Paladin

    private static void PaladinRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The order is the priority of The Balance guide: what must be pressed first goes on top.
        // Blade of Honor right after Blade of Valor, the Confiteor chain and Goring Blade first in the burst (they do not break the combo),
        // then the Atonement chain (Sepulchre, Supplication), Divine Might (Holy Spirit) and the Royal Authority combo.
        // Atonement (then Supplication, Sepulchre) and Confiteor (then Blade of Faith, Truth, Valor) are ONE button each that changes
        // into the next step: one tracker each, named after the button.
        b.Step(row, "Blade of Honor");
        b.Step(row, "Confiteor");
        b.Step(row, "Goring Blade", withTime: true);
        b.Step(row, "Atonement");
        b.Enhancement(row, "Divine Might");

        // Rage of Halone is upgraded into Royal Authority by the game: one tracker, named after the newest.
        b.Step(row, "Rage of Halone", "Royal Authority");
        b.Step(row, "Riot Blade");

        // The AoE combo (Total Eclipse, then Prominence): only the second step, after the single-target ones.
        b.Step(row, "Prominence");
    }

    private static void PaladinCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // From the longest cooldown to the shortest. No abilities light up here (no glow): the game turns the button of Requiescat into
        // Imperator by itself, so one ability is enough; its icon is shown while the effect is on, and the effect counts its stacks.
        b.Cd(row, "Imperator", new[] { "Requiescat" }, new[] { "Requiescat" }, glow: false, effectStacks: true, abilityIcon: true);
        b.Cd(row, "Fight or Flight", new[] { "Fight or Flight" }, new[] { "Fight or Flight" }, glow: false);
        b.Cd(row, "Circle of Scorn", new[] { "Circle of Scorn" }, glow: false);
        b.Cd(row, "Expiacion", new[] { "Spirits Within" }, glow: false);
        b.Cd(row, "Intervene", new[] { "Intervene" }, charges: true, glow: false);
    }

    private static void PaladinDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Defensives go from the longest cooldown to the shortest inside each kind: the big one, the ones that spend the gauge,
        // the ones that also cover the party, and the individual ones.
        b.Cd(row, "Hallowed Ground", new[] { "Hallowed Ground" }, new[] { "Hallowed Ground" }, glow: false);

        // They cost Oath Gauge (spender): dimmed while the game does not light them (not enough gauge), shown as they are when it does.
        // Cover can be cast on you by another Paladin, so only its effect applied by you counts. Intervention leaves its effect on the target only.
        b.Cd(row, "Cover", new[] { "Cover" }, new[] { "Cover" }, glow: false, spender: true, effectOnlyMine: true);
        b.Cd(row, "Intervention", new[] { "Intervention" }, glow: false, spender: true);
        b.Cd(row, "Holy Sheltron", new[] { "Sheltron" }, new[] { "Sheltron", "Holy Sheltron" }, glow: false, spender: true);

        b.Cd(row, "Passage of Arms", new[] { "Passage of Arms" }, new[] { "Passage of Arms" }, glow: false);

        // Another Paladin can put Divine Veil on you: only the one applied by you counts.
        b.Cd(row, "Divine Veil", new[] { "Divine Veil" }, new[] { "Divine Veil" }, glow: false, effectOnlyMine: true);

        // The game turns the button of Sentinel into Guardian by itself: one ability, and an effect for each version.
        b.Cd(row, "Guardian", new[] { "Sentinel" }, new[] { "Sentinel", "Guardian" }, glow: false);
        b.Cd(row, "Bulwark", new[] { "Bulwark" }, new[] { "Bulwark" }, glow: false);
    }

    // ---- Warrior (the owner's own setup, as the model)

    private static void WarriorRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The priority of The Balance guide, highest first: Primal Wrath (do not drop it), Primal Rend and then Primal Ruination (20 s),
        // the combo and, last, Surging Tempest (the upkeep: always shown, red while it is missing).
        b.Enhancement(row, "Wrathful");
        b.Enhancement(row, "Primal Rend Ready");
        b.Enhancement(row, "Primal Ruination Ready");

        // The steps of a combo go in reverse: the last one first.
        foreach (var step in new[] { "Storm's Path", "Maim", "Mythril Tempest" })
            b.Step(row, step);

        b.Upkeep(row, "Surging Tempest");
    }

    private static void WarriorCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Infuriate first: it is the one tied to the Beast Gauge, like a separate option. No abilities light up here (no glow).
        b.Cd(row, "Infuriate", new[] { "Infuriate" }, charges: true, glow: false);

        // The game turns the button of Berserk into Inner Release by itself: one ability, and an effect for each version.
        // Its icon is shown while the effect is on, and the effect counts its stacks.
        b.Cd(row, "Inner Release", new[] { "Berserk" }, new[] { "Berserk", "Inner Release" }, glow: false, effectStacks: true, abilityIcon: true);
        b.Cd(row, "Upheaval", new[] { "Upheaval" }, glow: false);
        b.Cd(row, "Onslaught", new[] { "Onslaught" }, charges: true, glow: false);
    }

    private static void WarriorDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Holmgang", new[] { "Holmgang" }, new[] { "Holmgang" }, glow: false);

        // The ones that also cover the party. Another Warrior can put Shake It Off on you: only the one applied by you counts.
        b.Cd(row, "Shake It Off", new[] { "Shake It Off" }, new[] { "Shake It Off" }, glow: false, effectOnlyMine: true);
        b.Cd(row, "Nascent Flash", new[] { "Nascent Flash" }, new[] { "Nascent Flash" }, glow: false);

        // The individual ones. The game turns Vengeance into Damnation and Raw Intuition into Bloodwhetting by itself: one ability each,
        // and an effect for each version.
        b.Cd(row, "Damnation", new[] { "Vengeance" }, new[] { "Vengeance", "Damnation" }, glow: false);
        b.Cd(row, "Thrill of Battle", new[] { "Thrill of Battle" }, new[] { "Thrill of Battle" }, glow: false);
        b.Cd(row, "Equilibrium", new[] { "Equilibrium" }, new[] { "Equilibrium" }, glow: false);
        b.Cd(row, "Bloodwhetting", new[] { "Raw Intuition" }, new[] { "Raw Intuition", "Bloodwhetting" }, glow: false);
    }

    // ---- Dark Knight (Blood and Darkside are in the gauge, Dark Arts is the frame of the Darkside bar)

    private static void DarkKnightRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The priority of The Balance guide: Disesteem (the Scorn status) and the Scarlet Delirium chain (Comeuppance and Torcleaver are
        // the same button, and Impalement is its AoE form) go under the enhancements, then the combo. Bloodspiller and Edge of Shadow have no
        // glow of their own and the gauge shows when to spend them.
        b.Enhancement(row, "Scorn");
        b.Step(row, "Scarlet Delirium");

        foreach (var step in new[] { "Souleater", "Syphon Strike", "Stalwart Soul" })
            b.Step(row, step);
    }

    private static void DarkKnightCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones tied to the Blood gauge first, then the rest from the longest cooldown to the shortest.
        b.Cd(row, "Delirium", new[] { "Delirium" }, new[] { "Delirium" }, effectStacks: true);
        b.Cd(row, "Blood Weapon", new[] { "Blood Weapon" }, new[] { "Blood Weapon" }, effectStacks: true);
        b.Cd(row, "Living Shadow", new[] { "Living Shadow" });
        b.Cd(row, "Salted Earth", new[] { "Salted Earth" }, new[] { "Salted Earth" });
        b.Cd(row, "Carve and Spit", new[] { "Abyssal Drain", "Carve and Spit" });
        b.Cd(row, "Shadowbringer", new[] { "Shadowbringer" }, charges: true);
    }

    private static void DarkKnightDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Living Dead", new[] { "Living Dead" }, new[] { "Living Dead", "Walking Dead" });

        // It spends MP.
        b.Cd(row, "The Blackest Night", new[] { "The Blackest Night" }, new[] { "Blackest Night" });

        // The one that also covers the party.
        b.Cd(row, "Dark Missionary", new[] { "Dark Missionary" }, new[] { "Dark Missionary" });

        // The individual ones.
        b.Cd(row, "Shadowed Vigil", new[] { "Shadow Wall", "Shadowed Vigil" }, new[] { "Shadow Wall", "Shadowed Vigil" });
        b.Cd(row, "Dark Mind", new[] { "Dark Mind" }, new[] { "Dark Mind" });
        b.Cd(row, "Oblation", new[] { "Oblation" }, new[] { "Oblation" }, charges: true);
    }

    // ---- Gunbreaker (the Cartridges are in the gauge)

    private static void GunbreakerRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ready actions: first the Continuation ones (oGCD, they run out fast and come after each step of the chain, so the last step first),
        // then the ones of the GCDs of the burst (Reign of Beasts, then Sonic Break: the guide spends Reign first when GCDs are lost).
        // The Gnashing Fang chain (Savage Claw, Wicked Talon) is the Gnashing Fang tracker of the CDs and the Reign chain (Noble Blood,
        // Lion Heart) is Ready To Reign.
        foreach (var ready in new[] { "Ready To Gouge", "Ready To Tear", "Ready To Rip", "Ready To Blast", "Ready To Raze", "Ready To Reign", "Ready To Break" })
            b.Enhancement(row, ready);

        // The combo goes in reverse (the last step first), the AoE one after the single-target ones.
        foreach (var step in new[] { "Solid Barrel", "Brutal Shell", "Demon Slaughter" })
            b.Step(row, step);
    }

    private static void GunbreakerCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Bloodfest", new[] { "Bloodfest" });
        b.Cd(row, "No Mercy", new[] { "No Mercy" }, new[] { "No Mercy" });
        b.Cd(row, "Double Down", new[] { "Double Down" });
        b.Cd(row, "Bow Shock", new[] { "Bow Shock" }, new[] { "Bow Shock" });
        b.Cd(row, "Gnashing Fang", new[] { "Gnashing Fang" }, charges: true);
        b.Cd(row, "Blasting Zone", new[] { "Danger Zone", "Blasting Zone" });
    }

    private static void GunbreakerDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Superbolide", new[] { "Superbolide" }, new[] { "Superbolide" });

        // The ones that also cover the party.
        b.Cd(row, "Heart of Light", new[] { "Heart of Light" }, new[] { "Heart of Light" });
        b.Cd(row, "Aurora", new[] { "Aurora" }, new[] { "Aurora" }, charges: true);

        // The individual ones.
        b.Cd(row, "Great Nebula", new[] { "Nebula", "Great Nebula" }, new[] { "Nebula", "Great Nebula" });
        b.Cd(row, "Camouflage", new[] { "Camouflage" }, new[] { "Camouflage" });
        b.Cd(row, "Heart of Corundum", new[] { "Heart of Stone", "Heart of Corundum" }, new[] { "Heart of Stone", "Heart of Corundum" });
    }

    // ---- White Mage (Lilies and Blood Lilies are in the gauge)

    private static void WhiteMageRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Dia always up, then Sacred Sight (Glare IV, with its stacks), then what the other cooldowns give and runs out
        // (Divine Grace lets Divine Caress follow Temperance).
        b.Enfeeblement(row, "Dia", "Aero II", "Aero");
        b.Enhancement(row, "Sacred Sight", stacks: true, iconAbility: "Glare IV");
        b.Enhancement(row, "Divine Grace");
        b.Enhancement(row, "Freecure");
    }

    private static void WhiteMageCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Presence of Mind", new[] { "Presence of Mind" }, new[] { "Presence of Mind" });
        b.Cd(row, "Thin Air", new[] { "Thin Air" }, new[] { "Thin Air" }, charges: true);
        b.Cd(row, "Assize", new[] { "Assize" });
    }

    private static void WhiteMageDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones that cover the party (Plenary Indulgence gives the status Confession), then Benediction, then the individual ones.
        b.Cd(row, "Liturgy of the Bell", new[] { "Liturgy of the Bell" }, new[] { "Liturgy of the Bell" }, effectStacks: true, abilityIcon: true);
        b.Cd(row, "Temperance", new[] { "Temperance" }, new[] { "Temperance" });
        b.Cd(row, "Asylum", new[] { "Asylum" }, new[] { "Asylum" });
        b.Cd(row, "Plenary Indulgence", new[] { "Plenary Indulgence" }, new[] { "Confession" });
        b.Cd(row, "Benediction", new[] { "Benediction" });

        b.Cd(row, "Aquaveil", new[] { "Aquaveil" }, new[] { "Aquaveil" });
        b.Cd(row, "Tetragrammaton", new[] { "Tetragrammaton" }, charges: true);
        b.Cd(row, "Divine Benison", new[] { "Divine Benison" }, new[] { "Divine Benison" }, charges: true);
    }

    private static void ScholarRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Biolysis always up, then what Chain Stratagem gives (Baneful Impaction) and Consolation, which only works while the Seraph is out.
        b.Enfeeblement(row, "Biolysis", "Bio II", "Bio");
        b.Enhancement(row, "Impact Imminent");
        b.Cd(row, "Consolation", new[] { "Consolation" }, charges: true);
    }

    private static void ScholarCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones that fill the Aetherflow first, then the rest from the longest cooldown to the shortest.
        b.Cd(row, "Dissipation", new[] { "Dissipation" }, new[] { "Dissipation" });
        b.Cd(row, "Aetherflow", new[] { "Aetherflow" });
        b.Cd(row, "Seraphism", new[] { "Seraphism" }, new[] { "Seraphism" });
        b.Cd(row, "Summon Seraph", new[] { "Summon Seraph" });
        b.Cd(row, "Chain Stratagem", new[] { "Chain Stratagem" });
        b.Cd(row, "Recitation", new[] { "Recitation" }, new[] { "Recitation" });
    }

    private static void ScholarDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones that spend Aetherflow (the stacks already show it: no glow), then the ones that cover the party, then the individual ones.
        b.Cd(row, "Excogitation", new[] { "Excogitation" }, new[] { "Excogitation" }, glow: false);
        b.Cd(row, "Sacred Soil", new[] { "Sacred Soil" }, new[] { "Sacred Soil" }, glow: false);
        b.Cd(row, "Indomitability", new[] { "Indomitability" }, glow: false);

        b.Cd(row, "Expedient", new[] { "Expedient" }, new[] { "Expedience" });
        b.Cd(row, "Deployment Tactics", new[] { "Deployment Tactics" });
        b.Cd(row, "Fey Illumination", new[] { "Fey Illumination" }, new[] { "Fey Illumination" });
        b.Cd(row, "Whispering Dawn", new[] { "Whispering Dawn" }, new[] { "Whispering Dawn" });
        b.Cd(row, "Fey Blessing", new[] { "Fey Blessing" });

        b.Cd(row, "Protraction", new[] { "Protraction" }, new[] { "Protraction" });
        b.Cd(row, "Emergency Tactics", new[] { "Emergency Tactics" }, new[] { "Emergency Tactics" });
    }

    private static void AstrologianRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Combust always up, then what the other cooldowns give and runs out: Divining (the Oracle, after Divination) and Suntouched (Sun Sign, after Neutral Sect).
        b.Enfeeblement(row, "Combust III", "Combust II", "Combust");
        b.Enhancement(row, "Divining");
        b.Enhancement(row, "Suntouched");
    }

    private static void AstrologianCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones that give cards first (Astral Draw and Minor Arcana, the gauge shows them), then the rest from the longest cooldown to the shortest.
        b.Cd(row, "Astral Draw", new[] { "Astral Draw", "Umbral Draw" });
        b.Cd(row, "Minor Arcana", new[] { "Minor Arcana" });
        b.Cd(row, "Divination", new[] { "Divination" }, new[] { "Divination" });
        b.Cd(row, "Lightspeed", new[] { "Lightspeed" }, new[] { "Lightspeed" }, charges: true);
    }

    private static void AstrologianDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The big one, then the ones that cover the party, then the individual ones.
        b.Cd(row, "Macrocosmos", new[] { "Macrocosmos" }, new[] { "Macrocosmos" });

        b.Cd(row, "Neutral Sect", new[] { "Neutral Sect" }, new[] { "Neutral Sect" });
        b.Cd(row, "Collective Unconscious", new[] { "Collective Unconscious" }, new[] { "Collective Unconscious" });
        b.Cd(row, "Celestial Opposition", new[] { "Celestial Opposition" });
        b.Cd(row, "Earthly Star", new[] { "Earthly Star" }, new[] { "Earthly Dominance", "Giant Dominance" });
        b.Cd(row, "Horoscope", new[] { "Horoscope" }, new[] { "Horoscope", "Horoscope Helios" });

        b.Cd(row, "Synastry", new[] { "Synastry" }, new[] { "Synastry" });
        b.Cd(row, "Exaltation", new[] { "Exaltation" }, new[] { "Exaltation" });
        b.Cd(row, "Essential Dignity", new[] { "Essential Dignity" }, charges: true);
        b.Cd(row, "Celestial Intersection", new[] { "Celestial Intersection" }, charges: true);
    }

    private static void SageRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The DoT always up, then Kardia (always on someone). Eukrasia is not tracked: it only changes Dosis, which is pressed anyway,
        // and the DoT shows red while it is missing.
        b.Enfeeblement(row, "Eukrasian Dosis III", "Eukrasian Dosis II", "Eukrasian Dosis");
        b.Upkeep(row, "Kardia");
    }

    private static void SageCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Rhizomata gives Addersgall (the gauge shows it): first. Then the rest from the longest cooldown to the shortest.
        b.Cd(row, "Rhizomata", new[] { "Rhizomata" });
        b.Cd(row, "Psyche", new[] { "Psyche" });
        b.Cd(row, "Phlegma III", new[] { "Phlegma III", "Phlegma II", "Phlegma" }, charges: true);
    }

    private static void SageDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The big one, then the ones that spend Addersgall (the stacks already show it: no glow), then the ones that cover the party,
        // then the individual ones.
        b.Cd(row, "Philosophia", new[] { "Philosophia" }, new[] { "Eudaimonia" });

        b.Cd(row, "Taurochole", new[] { "Taurochole" }, glow: false);
        b.Cd(row, "Kerachole", new[] { "Kerachole" }, new[] { "Kerachole" }, glow: false);
        b.Cd(row, "Ixochole", new[] { "Ixochole" }, glow: false);

        b.Cd(row, "Panhaima", new[] { "Panhaima" }, new[] { "Panhaima" });
        b.Cd(row, "Holos", new[] { "Holos" }, new[] { "Holos" });
        b.Cd(row, "Physis II", new[] { "Physis II" }, new[] { "Physis II" });

        b.Cd(row, "Haima", new[] { "Haima" }, new[] { "Haima" });
        b.Cd(row, "Soteria", new[] { "Soteria" }, new[] { "Soteria" }, effectStacks: true);
        b.Cd(row, "Zoe", new[] { "Zoe" }, new[] { "Zoe" });
        b.Cd(row, "Krasis", new[] { "Krasis" }, new[] { "Krasis" });
        b.Cd(row, "Pepsis", new[] { "Pepsis" });
    }

    // ================================================================ melee

    // ---- Monk 

    private static void MonkRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The follow-ups of the Riddles (Fire's Reply, Wind's Reply) are time-limited: first. Earth's Reply is the one of the defensive Riddle.
        // The forms are not tracked: they change with every button pressed.
        b.Enhancement(row, "Fire's Rumination");
        b.Enhancement(row, "Wind's Rumination");
        b.Enhancement(row, "Earth's Rumination");
    }

    private static void MonkCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Perfect Balance feeds the Masterful Blitz: first. Then from the longest cooldown to the shortest.
        // The Forbidden Chakra / Enlightenment (they spend Chakra) have no cooldown of their own.
        b.Cd(row, "Perfect Balance", new[] { "Perfect Balance" }, new[] { "Perfect Balance" }, charges: true, effectStacks: true);
        b.Cd(row, "Brotherhood", new[] { "Brotherhood" }, new[] { "Brotherhood" });
        b.Cd(row, "Riddle of Wind", new[] { "Riddle of Wind" }, new[] { "Riddle of Wind" });
        b.Cd(row, "Riddle of Fire", new[] { "Riddle of Fire" }, new[] { "Riddle of Fire" });
    }

    private static void MonkDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The one that covers the party, then the individual one.
        b.Cd(row, "Mantra", new[] { "Mantra" }, new[] { "Mantra" });
        b.Cd(row, "Riddle of Earth", new[] { "Riddle of Earth" }, new[] { "Earth's Resolve" });
    }

    // ---- Dragoon (Winged Glide and Elusive Jump have no damage: they are in "General")

    private static void DragoonRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The DoT and Power Surge always up, then what the other buttons give (the Draconian Fire combo, Mirage Dive, the Life of the Dragon
        // follow-ups, Wyrmwind Thrust), then the combo finishers. The single-target ones first, then the AoE one.
        b.Enfeeblement(row, "Chaotic Spring", "Chaos Thrust");
        b.Upkeep(row, "Power Surge");
        b.Enhancement(row, "Draconian Fire");
        b.Enhancement(row, "Dive Ready");
        b.Enhancement(row, "Nastrond Ready");
        b.Enhancement(row, "Starcross Ready");
        b.Enhancement(row, "Firstminds' Focus", stacks: true);

        foreach (var step in new[] { "Wheeling Thrust", "Fang and Claw" })
            b.Step(row, step);

        // Full Thrust is upgraded into Heavens' Thrust by the game: one tracker, named after the newest.
        b.Step(row, "Full Thrust", "Heavens' Thrust");
        b.Step(row, "Coerthan Torment");
    }

    private static void DragoonCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // From the longest cooldown to the shortest. The jumps hit, so they stay here.
        b.Cd(row, "Dragonfire Dive", new[] { "Dragonfire Dive" });
        b.Cd(row, "Battle Litany", new[] { "Battle Litany" }, new[] { "Battle Litany" });
        b.Cd(row, "Lance Charge", new[] { "Lance Charge" }, new[] { "Lance Charge" });
        b.Cd(row, "Geirskogul", new[] { "Geirskogul" }, new[] { "Life of the Dragon" });
        b.Cd(row, "Life Surge", new[] { "Life Surge" }, new[] { "Life Surge" }, charges: true);
        b.Cd(row, "High Jump", new[] { "Jump", "High Jump" });
    }

    // ---- Ninja

    private static void NinjaRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What the other buttons give and runs out (Raiju, Phantom Kamaitachi, Tenri Jindo), the finishers of the combo (Armor Crush
        // and Aeolian Edge, then the AoE one). Bhavacakra, Zesho Meppo and the Hellfrog/Deathfrog Medium spend Ninki: the gauge will show it.
        b.Enhancement(row, "Shadow Walker");
        b.Enhancement(row, "Tenri Jindo Ready");
        b.Enhancement(row, "Phantom Kamaitachi Ready");
        b.Enhancement(row, "Raiju Ready", stacks: true);

        foreach (var step in new[] { "Armor Crush", "Aeolian Edge", "Hakke Mujinsatsu" })
            b.Step(row, step);
    }

    private static void NinjaCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Bunshin spends Ninki: first. Then from the longest cooldown to the shortest. (The mudras are added by the safety net.)
        b.Cd(row, "Bunshin", new[] { "Bunshin" }, new[] { "Bunshin" }, effectStacks: true);
        b.Cd(row, "Dokumori", new[] { "Dokumori" });
        b.Cd(row, "Ten Chi Jin", new[] { "Ten Chi Jin" }, new[] { "Ten Chi Jin" });
        b.Cd(row, "Kunai's Bane", new[] { "Kunai's Bane" });
        b.Cd(row, "Kassatsu", new[] { "Kassatsu" }, new[] { "Kassatsu" });
    }

    private static void NinjaDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Shade Shift", new[] { "Shade Shift" }, new[] { "Shade Shift" });
    }

    // ---- Samurai

    private static void SamuraiRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Fugetsu and Fuka always up, then Higanbana on the target, then what the other buttons give (Ogi Namikiri and its Kaeshi, Zanshin),
        // then the finishers of the combos: the single-target ones first, then the AoE ones.
        b.Upkeep(row, "Fugetsu");
        b.Upkeep(row, "Fuka");
        b.Enfeeblement(row, "Higanbana");
        b.Enhancement(row, "Ogi Namikiri Ready");
        b.Enhancement(row, "Kaeshi: Namikiri Ready");
        b.Enhancement(row, "Zanshin Ready");

        foreach (var step in new[] { "Yukikaze", "Kasha", "Gekko", "Oka", "Mangetsu" })
            b.Step(row, step);
    }

    private static void SamuraiCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Ikishoten gives Kenki and Senei spends it (no glow: the gauge will show it): first. Then from the longest cooldown to the shortest.
        // Guren shares its cooldown with Senei, so one tracker is enough. Gyoten hits, so it stays here.
        b.Cd(row, "Ikishoten", new[] { "Ikishoten" });
        b.Cd(row, "Hissatsu: Senei", new[] { "Hissatsu: Senei" }, glow: false);
        b.Cd(row, "Meikyo Shisui", new[] { "Meikyo Shisui" }, new[] { "Meikyo Shisui" }, charges: true, effectStacks: true);
        b.Cd(row, "Hissatsu: Gyoten", new[] { "Hissatsu: Gyoten" }, glow: false);
    }

    private static void SamuraiDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Tengentsu", new[] { "Third Eye", "Tengentsu" }, new[] { "Third Eye", "Tengentsu" });
    }

    // ---- Reaper (Hell's Ingress and Hell's Egress are in "General")

    private static void ReaperRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Death's Design always up, then the Soul Reaver buttons, the positional enhancements, Perfectio, the Plentiful Harvest and the combo.
        // Enshrouded is shown in the Enshroud tracker (CDs row).
        b.Enfeeblement(row, "Death's Design");
        b.Enhancement(row, "Soul Reaver", stacks: true);
        b.Enhancement(row, "Executioner", stacks: true);
        b.Enhancement(row, "Perfectio Parata");
        b.Enhancement(row, "Immortal Sacrifice", stacks: true);
        b.Enhancement(row, "Enhanced Gibbet");
        b.Enhancement(row, "Enhanced Gallows");

        foreach (var step in new[] { "Infernal Slice", "Nightmare Scythe" })
            b.Step(row, step);
    }

    private static void ReaperCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones tied to the Soul and Shroud gauges first (Soul Slice gives Soul, Gluttony and Enshroud spend them: no glow), then Arcane Circle.
        b.Cd(row, "Soul Slice", new[] { "Soul Slice" }, charges: true);
        b.Cd(row, "Gluttony", new[] { "Gluttony" }, glow: false);
        b.Cd(row, "Enshroud", new[] { "Enshroud" }, new[] { "Enshrouded" }, glow: false, effectStacks: true);
        b.Cd(row, "Arcane Circle", new[] { "Arcane Circle" }, new[] { "Arcane Circle" });
    }

    private static void ReaperDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Arcane Crest", new[] { "Arcane Crest" }, new[] { "Crest of Time Borrowed" });
    }

    // ---- Viper (Slither is in "General")

    private static void ViperRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Hunter's Instinct and Swiftscaled always up, then Reawaken, then the Vicewinder/Vicepit buttons (single target, then AoE),
        // their Twinfang/Twinblood follow-ups and the finishers of the combos (single target, then AoE).
        b.Upkeep(row, "Hunter's Instinct");
        b.Upkeep(row, "Swiftscaled");
        b.Enhancement(row, "Reawakened");
        b.Enhancement(row, "Ready to Reawaken");

        foreach (var step in new[] { "Swiftskin's Coil", "Hunter's Coil", "Swiftskin's Den", "Hunter's Den" })
            b.Step(row, step);

        b.Enhancement(row, "Poised for Twinblood");
        b.Enhancement(row, "Poised for Twinfang");

        foreach (var step in new[] { "Hindsbane Fang", "Hindsting Strike", "Flankbane Fang", "Flanksting Strike", "Bloodied Maw", "Jagged Maw" })
            b.Step(row, step);
    }

    private static void ViperCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Serpent's Ire gives Rattling Coils: first. Vicepit shares its cooldown with Vicewinder, so one tracker is enough.
        b.Cd(row, "Serpent's Ire", new[] { "Serpent's Ire" });
        b.Cd(row, "Vicewinder", new[] { "Vicewinder" }, charges: true);
    }

    // ================================================================ physical ranged

    // ---- Bard (Repelling Shot is in "General")

    private static void BardRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The two DoTs always up, then what the other buttons give (Blast Arrow, Resonant Arrow, Radiant Encore), then Hawk's Eye.
        b.Enfeeblement(row, "Stormbite", "Windbite");
        b.Enfeeblement(row, "Caustic Bite", "Venomous Bite");
        b.Enhancement(row, "Blast Arrow Ready");
        b.Enhancement(row, "Resonant Arrow Ready");
        b.Enhancement(row, "Radiant Encore Ready");
        b.Enhancement(row, "Hawk's Eye");
    }

    private static void BardCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The three songs share their cooldown: one tracker that shows the song that is playing and then the cooldown. They are tied to the
        // gauge, so first. Then from the longest cooldown to the shortest.
        b.Cd(row, "Wanderer's Minuet", new[] { "Wanderer's Minuet" }, new[] { "Wanderer's Minuet", "Mage's Ballad", "Army's Paeon" });
        b.Cd(row, "Raging Strikes", new[] { "Raging Strikes" }, new[] { "Raging Strikes" });
        b.Cd(row, "Battle Voice", new[] { "Battle Voice" }, new[] { "Battle Voice" });
        b.Cd(row, "Barrage", new[] { "Barrage" }, new[] { "Barrage" });
        b.Cd(row, "Radiant Finale", new[] { "Radiant Finale" }, new[] { "Radiant Finale" });
        b.Cd(row, "Sidewinder", new[] { "Sidewinder" });
        b.Cd(row, "Empyreal Arrow", new[] { "Empyreal Arrow" });
        // Rain of Death shares its charges with Bloodletter: one tracker.
        b.Cd(row, "Heartbreak Shot", new[] { "Bloodletter", "Heartbreak Shot" }, charges: true);
    }

    private static void BardDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The one that covers the party, then the individual ones.
        b.Cd(row, "Troubadour", new[] { "Troubadour" }, new[] { "Troubadour" });
        b.Cd(row, "Nature's Minne", new[] { "Nature's Minne" }, new[] { "Nature's Minne" });
        b.Cd(row, "The Warden's Paean", new[] { "The Warden's Paean" });
    }

    // ---- Machinist

    private static void MachinistRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What the other buttons give and runs out (Full Metal Field, Excavator, a free Hypercharge) and the finisher of the combo.
        b.Enhancement(row, "Full Metal Machinist");
        b.Enhancement(row, "Excavator Ready");
        b.Enhancement(row, "Hypercharged");
        b.Step(row, "Clean Shot", "Heated Clean Shot");
    }

    private static void MachinistCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The ones tied to Heat and Battery first (Barrel Stabilizer gives Heat, Hypercharge and the Automaton Queen spend them: no glow),
        // then from the longest cooldown to the shortest.
        b.Cd(row, "Barrel Stabilizer", new[] { "Barrel Stabilizer" });
        b.Cd(row, "Hypercharge", new[] { "Hypercharge" }, new[] { "Overheated" }, glow: false, effectStacks: true);
        b.Cd(row, "Automaton Queen", new[] { "Rook Autoturret", "Automaton Queen" }, glow: false);
        b.Cd(row, "Wildfire", new[] { "Wildfire" });
        b.Cd(row, "Chain Saw", new[] { "Chain Saw" });
        b.Cd(row, "Reassemble", new[] { "Reassemble" }, new[] { "Reassembled" }, charges: true);
        b.Cd(row, "Air Anchor", new[] { "Air Anchor" });
        b.Cd(row, "Double Check", new[] { "Gauss Round", "Double Check" }, charges: true);
        b.Cd(row, "Checkmate", new[] { "Ricochet", "Checkmate" }, charges: true);
        b.Cd(row, "Drill", new[] { "Drill" }, charges: true);
    }

    private static void MachinistDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The one that covers the party, then the individual one.
        b.Cd(row, "Tactician", new[] { "Tactician" }, new[] { "Tactician" });
        b.Cd(row, "Dismantle", new[] { "Dismantle" });
    }

    // ---- Dancer (En Avant is in "General")

    private static void DancerRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Standard Finish always up, then what the other buttons give, the most important first (Dance of the Dawn, Tillana, Starfall Dance,
        // Last Dance, Finishing Move, the Fan Dances and the ready actions of the combos).
        b.Upkeep(row, "Standard Finish");
        b.Enhancement(row, "Dance of the Dawn Ready");
        b.Enhancement(row, "Flourishing Finish");
        b.Enhancement(row, "Flourishing Starfall");
        b.Enhancement(row, "Last Dance Ready");
        b.Enhancement(row, "Finishing Move Ready");
        b.Enhancement(row, "Fourfold Fan Dance");
        b.Enhancement(row, "Threefold Fan Dance");
        b.Enhancement(row, "Silken Flow");
        b.Enhancement(row, "Silken Symmetry");
        b.Enhancement(row, "Flourishing Flow");
        b.Enhancement(row, "Flourishing Symmetry");
    }

    private static void DancerCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Flourish gives the Fan Dance ready actions (the Feathers): first. Then from the longest cooldown to the shortest.
        b.Cd(row, "Flourish", new[] { "Flourish" });
        b.Cd(row, "Technical Step", new[] { "Technical Step" }, new[] { "Technical Finish" });
        b.Cd(row, "Devilment", new[] { "Devilment" }, new[] { "Devilment" });
        b.Cd(row, "Standard Step", new[] { "Standard Step" });
    }

    private static void DancerDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // All of them cover the party: from the longest cooldown to the shortest.
        b.Cd(row, "Improvisation", new[] { "Improvisation" }, new[] { "Improvisation" });
        b.Cd(row, "Shield Samba", new[] { "Shield Samba" }, new[] { "Shield Samba" });
        b.Cd(row, "Curing Waltz", new[] { "Curing Waltz" });
    }

    // ================================================================ magical ranged

    // ---- Black Mage (Aetherial Manipulation is in "General")

    private static void BlackMageRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The Thunder DoT always up, then what the other buttons give (Firestarter, Thunderhead) and Paradox.
        // Flare Star and the Polyglot spells depend on the gauge.
        b.Enfeeblement(row, "High Thunder", "Thunder III", "Thunder");
        b.Enhancement(row, "Firestarter");
        b.Enhancement(row, "Thunderhead");
        b.Step(row, "Paradox");
    }

    private static void BlackMageCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Amplifier gives Polyglot and Manafont gives MP: first. Then from the longest cooldown to the shortest.
        b.Cd(row, "Amplifier", new[] { "Amplifier" });
        b.Cd(row, "Manafont", new[] { "Manafont" });
        b.Cd(row, "Ley Lines", new[] { "Ley Lines" }, new[] { "Ley Lines" });
        b.Cd(row, "Triplecast", new[] { "Triplecast" }, new[] { "Triplecast" }, effectStacks: true);
    }

    private static void BlackMageDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Manaward", new[] { "Manaward" }, new[] { "Manaward" });
    }

    // ---- Summoner

    private static void SummonerRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What the summons and the other buttons give and runs out: the Astral Flow of Ifrit (Crimson Cyclone and then Crimson Strike),
        // Garuda's and Titan's favors, and Ruin IV.
        b.Enhancement(row, "Ifrit's Favor");
        b.Enhancement(row, "Crimson Strike Ready");
        b.Enhancement(row, "Garuda's Favor");
        b.Enhancement(row, "Titan's Favor");
        b.Enhancement(row, "Further Ruin", stacks: true);
    }

    private static void SummonerCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Energy Drain gives Aetherflow: first. Then from the longest cooldown to the shortest. Phoenix shares its cooldown with the
        // other demi-summons, so one tracker is enough.
        b.Cd(row, "Energy Drain", new[] { "Energy Drain" });
        b.Cd(row, "Searing Light", new[] { "Searing Light" }, new[] { "Searing Light" });
        b.Cd(row, "Summon Solar Bahamut", new[] { "Summon Bahamut", "Summon Solar Bahamut" });
    }

    private static void SummonerDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // The one that covers the party, then the individual one.
        b.Cd(row, "Lux Solaris", new[] { "Lux Solaris" });
        b.Cd(row, "Radiant Aegis", new[] { "Radiant Aegis" }, new[] { "Radiant Aegis" }, charges: true);
    }

    // ---- Red Mage (White and Black Mana and the stacks are in the gauge)

    private static void RedMageRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What the other buttons give and runs out, the most important first (Grand Impact, Prefulgence, Vice of Thorns, the melee combo
        // with Magicked Swordplay, then Verfire and Verstone), then the finishers that follow Verflare and Verholy.
        // Dualcast is not tracked: it only makes the next spell instant, which the progress bar already shows.
        b.Enhancement(row, "Grand Impact Ready");
        b.Enhancement(row, "Prefulgence Ready");
        b.Enhancement(row, "Thorned Flourish");
        b.Enhancement(row, "Magicked Swordplay", stacks: true);
        b.Enhancement(row, "Verfire Ready");
        b.Enhancement(row, "Verstone Ready");

        foreach (var step in new[] { "Resolution", "Scorch", "Verholy", "Verflare" })
            b.Step(row, step);
    }

    private static void RedMageCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Manafication gives mana: first. Then from the longest cooldown to the shortest. Corps-a-corps and Engagement hit, so they stay here.
        // Displacement shares its charges with Engagement, so one tracker is enough.
        b.Cd(row, "Manafication", new[] { "Manafication" }, new[] { "Manafication" }, effectStacks: true);
        b.Cd(row, "Embolden", new[] { "Embolden" }, new[] { "Embolden" });
        b.Cd(row, "Acceleration", new[] { "Acceleration" }, new[] { "Acceleration" }, charges: true);
        b.Cd(row, "Corps-a-corps", new[] { "Corps-a-corps" }, charges: true);
        b.Cd(row, "Engagement", new[] { "Engagement" }, charges: true);
        b.Cd(row, "Contre Sixte", new[] { "Contre Sixte" });
        b.Cd(row, "Fleche", new[] { "Fleche" });
    }

    private static void RedMageDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Cd(row, "Magick Barrier", new[] { "Magick Barrier" }, new[] { "Magick Barrier" });
    }

    // ---- Pictomancer (Smudge is in "General")

    private static void PictomancerRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What the Muses and the other buttons give and runs out, the most important first.
        b.Enhancement(row, "Starstruck");
        b.Enhancement(row, "Hammer Time", stacks: true);
        b.Enhancement(row, "Rainbow Bright");
        b.Enhancement(row, "Hyperphantasia", stacks: true);
        b.Enhancement(row, "Monochrome Tones");
        b.Enhancement(row, "Subtractive Spectrum");
    }

    private static void PictomancerCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // From the longest cooldown to the shortest.
        b.Cd(row, "Starry Muse", new[] { "Starry Muse" }, new[] { "Starry Muse" });
        b.Cd(row, "Striking Muse", new[] { "Striking Muse" }, charges: true);
        b.Cd(row, "Living Muse", new[] { "Living Muse" }, charges: true);
    }

    private static void PictomancerDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Tempera Grassa shares its cooldown with it and is only its follow-up.
        b.Cd(row, "Tempera Coat", new[] { "Tempera Coat" }, new[] { "Tempera Coat" });
    }

    // ---- Blue Mage (the spells of the solo build; a spell that is not in the active set never shows)

    private static void BlueMageRotation(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // What stays on you while you play: shown only while it is on, no glow (they are on almost all the time).
        b.StatusTracker(row, "Aetheric Mimicry: Tank");
        b.StatusTracker(row, "Basic Instinct");
        b.StatusTracker(row, "Mighty Guard");
        b.StatusTracker(row, "Devour");
        b.StatusTracker(row, "Rehydration");
    }

    private static void BlueMageCooldowns(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        // Shown only while the effect is on or the spell recharges. The vibe check first (The Ram's Voice, then Ultravibration), then the rest.
        b.Spell(row, "The Ram's Voice");
        b.Spell(row, "Ultravibration");
        b.Spell(row, "Doom");
        b.Spell(row, "Peat Pelt");
        b.Spell(row, "Deep Clean");
        b.Spell(row, "Mortal Flame");
        b.Spell(row, "Moon Flute", "Waxing Nocturne");
        b.Spell(row, "Sea Shanty");
        b.Spell(row, "Dimensional Shift");
    }

    private static void BlueMageDefensives(TrackerPresets.Builder b, TrackerGroupRuntime row)
    {
        b.Spell(row, "Dragon Force", "Dragon Force");
        b.Spell(row, "White Wind");
    }
}
