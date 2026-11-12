using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;

namespace NahjaUI.Modules.Trackers;

/// <summary>What a cooldown tracker is doing right now.</summary>
internal enum TrackerPhase
{
    /// <summary>Not a cooldown tracker.</summary>
    None,

    /// <summary>The ability can be used.</summary>
    Ready,

    /// <summary>No charge is available: the ability is recharging.</summary>
    Cooling,

    /// <summary>The effect of the ability is on right now (for example a defensive enhancement).</summary>
    Active
}

/// <summary>The result of evaluating one tracker for one frame.</summary>
/// <param name="Visible">Whether the icon is drawn.</param>
/// <param name="StatusPresent">Status trackers: the status is there. Cooldown trackers: the effect is active.</param>
/// <param name="Remaining">Seconds to show in the timer (status time, recharge time or effect time).</param>
/// <param name="Stacks">Status trackers: stack count.</param>
/// <param name="Phase">Cooldown trackers: ready, cooling or active.</param>
/// <param name="Total">Cooldown trackers: seconds one charge takes to recharge (for the sweep).</param>
/// <param name="Charges">Cooldown trackers: charges available.</param>
/// <param name="MaxCharges">Cooldown trackers: charges the ability can hold.</param>
/// <param name="Highlighted">Cooldown trackers: the game is making the button of the current form glow.</param>
/// <param name="AbilityBased">The trigger that matched works with an ability (not a status).</param>
/// <param name="Rule">The icon trigger that matched: it decides the picture and the glow.</param>
/// <param name="FoundStatusId">Status triggers that use a filter: the status of the filter that was found (it gives the icon and the stacks).</param>
/// <param name="IconActionId">Cooldown trackers: the action whose icon is shown (the form the game shows now; 0 = the chosen one).</param>
internal readonly record struct TrackerState(
    bool Visible,
    bool StatusPresent,
    float Remaining,
    int Stacks,
    TrackerPhase Phase = TrackerPhase.None,
    float Total = 0f,
    int Charges = 0,
    int MaxCharges = 1,
    uint IconActionId = 0,
    bool Highlighted = false,
    bool AbilityBased = false,
    TrackerRule? Rule = null,
    uint FoundStatusId = 0)
{
    public static readonly TrackerState Hidden = new(false, false, 0f, 0);
}

/// <summary>A tracker together with its layout element and its latest state.</summary>
internal sealed class TrackerRuntime
{
    public TrackerRuntime(TrackerDefinition definition, LayoutElement element)
    {
        Definition = definition;
        Element = element;
    }

    public TrackerDefinition Definition { get; }

    /// <summary>Holds the size of the icon, and its position when it is not inside a group.</summary>
    public LayoutElement Element { get; }

    public TrackerState State { get; set; } = TrackerState.Hidden;
    public int ConsecutiveErrors { get; set; }
}

/// <summary>A group together with its layout element (the box that is moved in unlock mode).</summary>
internal sealed class TrackerGroupRuntime
{
    public TrackerGroupRuntime(TrackerGroupDefinition definition, LayoutElement element)
    {
        Definition = definition;
        Element = element;
    }

    public TrackerGroupDefinition Definition { get; }
    public LayoutElement Element { get; }

    /// <summary>The enabled trackers of this group, in display order. Rebuilt every frame.</summary>
    public List<TrackerRuntime> Members { get; } = new();
}

/// <summary>
/// Every frame: evaluate each enabled tracker against the game snapshot, line up the trackers of each group,
/// and draw the visible icons. It only reads the snapshot and never calls Dalamud game services directly.
/// </summary>
internal sealed class TrackerEngine
{
    // A tracker that throws this many errors in a row is switched off until the plugin is reloaded.
    private const int MaxConsecutiveErrors = 5;

    // A cooldown up to this many seconds is the global cooldown, not a real cooldown.
    private const float GlobalCooldownSeconds = 3.5f;

    private readonly Plugin plugin;
    private readonly List<TrackerRuntime> runtimes = new();
    private readonly List<TrackerGroupRuntime> groups = new();
    private readonly List<TrackerRuntime> shown = new();
    private readonly HashSet<uint> watchedActions = new();

    public TrackerEngine(Plugin plugin)
    {
        this.plugin = plugin;
        LoadFromConfiguration();
    }

    /// <summary>
    /// Forgets every tracker and group in memory and builds them again from the configuration. Used after a profile is
    /// imported. Saved positions are kept (only the elements are detached, not forgotten).
    /// </summary>
    public void ReloadFromConfiguration()
    {
        foreach (var runtime in runtimes)
            plugin.Layout.Detach(runtime.Element.Id);

        foreach (var group in groups)
            plugin.Layout.Detach(group.Element.Id);

        runtimes.Clear();
        groups.Clear();
        shown.Clear();

        LoadFromConfiguration();
    }

    private void LoadFromConfiguration()
    {
        foreach (var definition in plugin.Configuration.TrackerGroups)
            RegisterGroup(definition);

        foreach (var definition in plugin.Configuration.Trackers)
        {
            // Forget a group that no longer exists.
            if (definition.GroupId is not null && FindGroup(definition.GroupId) is null)
                definition.GroupId = null;

            Register(definition);
        }

        ReconcileRootOrder();
    }

    public IReadOnlyList<TrackerRuntime> Trackers => runtimes;
    public IReadOnlyList<TrackerGroupRuntime> Groups => groups;

    internal Plugin Plugin => plugin;

    public TrackerGroupRuntime? FindGroup(string? id)
    {
        return id is null ? null : groups.FirstOrDefault(g => g.Definition.Id == id);
    }

    // ---------------------------------------------------------------- creating and removing

    /// <summary>
    /// Creates a new, empty tracker. It goes into the given group, or to the end of the top level of the list.
    /// </summary>
    public TrackerRuntime AddNew(TrackerGroupRuntime? intoGroup = null)
    {
        var definition = new TrackerDefinition();
        definition.Rules.Add(NewRule());
        plugin.Configuration.Trackers.Add(definition);

        var runtime = Register(definition);

        if (intoGroup is null)
            plugin.Configuration.RootOrder.Add(TrackerKey(definition));
        else
            MoveTrackerIntoGroup(runtime, intoGroup, null, false);

        plugin.RequestSave();
        return runtime;
    }

    /// <summary>A new trigger: it tracks nothing yet and has one text, the time.</summary>
    public static TrackerRule NewRule() => new()
    {
        Texts = { new TrackerTextDefinition { Sources = { new TrackerTextSource() } } }
    };

    /// <summary>Makes a copy of a tracker and puts it right below the original (inside the same group, or at the top level).</summary>
    public TrackerRuntime Duplicate(TrackerRuntime original)
    {
        // A deep copy through JSON: every setting of the tracker, now and in the future, is copied without listing them here.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original.Definition);
        var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<TrackerDefinition>(json) ?? new TrackerDefinition();
        definition.Id = Guid.NewGuid().ToString("N");
        definition.Name = string.IsNullOrWhiteSpace(original.Definition.Name) ? definition.Name : $"{original.Definition.Name} (copy)";

        plugin.Configuration.Trackers.Add(definition);
        var runtime = Register(definition);

        // Same size and place as the original (a loose tracker has a place of its own).
        runtime.Element.Layout.CopyPositionFrom(original.Element.Layout);
        runtime.Element.Layout.Width = original.Element.Layout.Width;
        runtime.Element.Layout.Height = original.Element.Layout.Height;

        var group = FindGroup(definition.GroupId);
        if (group is not null)
        {
            MoveTrackerIntoGroup(runtime, group, original, true);
        }
        else
        {
            definition.GroupId = null;
            PlaceAtRoot(TrackerKey(definition), TrackerKey(original.Definition), true);
        }

        plugin.RequestSave();
        return runtime;
    }

    public void Remove(TrackerRuntime runtime)
    {
        plugin.Configuration.Trackers.Remove(runtime.Definition);
        plugin.Configuration.RootOrder.Remove(TrackerKey(runtime.Definition));
        runtimes.Remove(runtime);
        plugin.Layout.Unregister(runtime.Element.Id);
        plugin.RequestSave();
    }

    public TrackerGroupRuntime AddGroup()
    {
        var definition = new TrackerGroupDefinition { IconWidth = 44, IconHeight = 44, Template = new TrackerDefinition() };
        plugin.Configuration.TrackerGroups.Add(definition);
        plugin.Configuration.RootOrder.Add(GroupKey(definition));

        var runtime = RegisterGroup(definition);
        plugin.RequestSave();
        return runtime;
    }

    /// <summary>Makes a copy of a group with a copy of each of its trackers. It goes right below the original in the list and in the same place on screen.</summary>
    public TrackerGroupRuntime DuplicateGroup(TrackerGroupRuntime original)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original.Definition);
        var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<TrackerGroupDefinition>(json) ?? new TrackerGroupDefinition();
        definition.Id = Guid.NewGuid().ToString("N");
        definition.Name = string.IsNullOrWhiteSpace(original.Definition.Name) ? definition.Name : $"{original.Definition.Name} (copy)";

        plugin.Configuration.TrackerGroups.Add(definition);
        var copy = RegisterGroup(definition);
        copy.Element.Layout.CopyPositionFrom(original.Element.Layout);
        PlaceAtRoot(GroupKey(definition), GroupKey(original.Definition), true);

        TrackerRuntime? previous = null;
        foreach (var member in MembersOf(original).ToList())
        {
            var memberJson = Newtonsoft.Json.JsonConvert.SerializeObject(member.Definition);
            var memberDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<TrackerDefinition>(memberJson) ?? new TrackerDefinition();
            memberDefinition.Id = Guid.NewGuid().ToString("N");
            memberDefinition.GroupId = definition.Id;

            plugin.Configuration.Trackers.Add(memberDefinition);
            var runtime = Register(memberDefinition);
            runtime.Element.Layout.CopyPositionFrom(member.Element.Layout);
            MoveTrackerIntoGroup(runtime, copy, previous, true);
            previous = runtime;
        }

        plugin.RequestSave();
        return copy;
    }

    /// <summary>Deletes a group together with all its trackers.</summary>
    public void RemoveGroupWithTrackers(TrackerGroupRuntime group)
    {
        foreach (var member in MembersOf(group).ToList())
            Remove(member);

        RemoveGroup(group);
    }

    /// <summary>Deletes a group. Its trackers are kept: they take the place of the group at the top level.</summary>
    public void RemoveGroup(TrackerGroupRuntime group)
    {
        var order = plugin.Configuration.RootOrder;
        var groupKey = GroupKey(group.Definition);

        var index = order.IndexOf(groupKey);
        if (index < 0)
            index = order.Count;

        order.Remove(groupKey);

        foreach (var member in MembersOf(group))
        {
            member.Definition.GroupId = null;
            order.Insert(Math.Min(index, order.Count), TrackerKey(member.Definition));
            index++;
        }

        plugin.Configuration.TrackerGroups.Remove(group.Definition);
        groups.Remove(group);
        plugin.Layout.Unregister(group.Element.Id);
        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- the explorer tree

    public static string TrackerKey(TrackerDefinition definition) => $"tracker:{definition.Id}";

    public static string GroupKey(TrackerGroupDefinition definition) => $"group:{definition.Id}";

    /// <summary>One entry at the top level of the list: a group or a loose tracker.</summary>
    internal readonly record struct RootEntry(TrackerGroupRuntime? Group, TrackerRuntime? Tracker);

    /// <summary>The top level of the list in the order the user chose, with groups and loose trackers mixed.</summary>
    internal List<RootEntry> RootEntries()
    {
        var result = new List<RootEntry>();

        foreach (var key in plugin.Configuration.RootOrder)
        {
            if (key.StartsWith("group:", StringComparison.Ordinal))
            {
                var group = FindGroup(key["group:".Length..]);
                if (group is not null)
                    result.Add(new RootEntry(group, null));
            }
            else if (key.StartsWith("tracker:", StringComparison.Ordinal))
            {
                var id = key["tracker:".Length..];
                var tracker = runtimes.FirstOrDefault(r => r.Definition.Id == id);

                if (tracker is not null && tracker.Definition.GroupId is null)
                    result.Add(new RootEntry(null, tracker));
            }
        }

        return result;
    }

    /// <summary>The trackers of a group, in display order.</summary>
    internal List<TrackerRuntime> MembersOf(TrackerGroupRuntime group)
    {
        return runtimes.Where(r => r.Definition.GroupId == group.Definition.Id).ToList();
    }

    /// <summary>Moves a group to the top level, before or after another top-level entry.</summary>
    public void MoveGroup(TrackerGroupRuntime group, string targetKey, bool after)
    {
        PlaceAtRoot(GroupKey(group.Definition), targetKey, after);
        plugin.RequestSave();
    }

    /// <summary>
    /// Takes a tracker out of its group (if it was in one) and puts it at the top level,
    /// before or after another top-level entry.
    /// </summary>
    public void MoveTrackerToRoot(TrackerRuntime tracker, string targetKey, bool after)
    {
        tracker.Definition.GroupId = null;
        PlaceAtRoot(TrackerKey(tracker.Definition), targetKey, after);
        plugin.RequestSave();
    }

    /// <summary>
    /// Puts a tracker inside a group, before or after one of the group's trackers
    /// (at the end of the group when no neighbour is given). A tracker that joins a group takes the group's icon size.
    /// </summary>
    public void MoveTrackerIntoGroup(TrackerRuntime tracker, TrackerGroupRuntime group, TrackerRuntime? neighbour, bool after)
    {
        if (neighbour == tracker)
            return;

        var joining = tracker.Definition.GroupId != group.Definition.Id;

        plugin.Configuration.RootOrder.Remove(TrackerKey(tracker.Definition));
        tracker.Definition.GroupId = group.Definition.Id;

        // The order inside a group is the order of the tracker list: take the tracker out and put it back in the right place.
        var configList = plugin.Configuration.Trackers;
        runtimes.Remove(tracker);
        configList.Remove(tracker.Definition);

        int index;
        if (neighbour is null)
        {
            var last = runtimes.LastOrDefault(r => r.Definition.GroupId == group.Definition.Id);
            index = last is null ? runtimes.Count : runtimes.IndexOf(last) + 1;
        }
        else
        {
            index = runtimes.IndexOf(neighbour) + (after ? 1 : 0);
        }

        runtimes.Insert(index, tracker);
        configList.Insert(index, tracker.Definition);

        if (joining)
            ApplyGroupStyleTo(group, tracker);

        plugin.RequestSave();
    }

    /// <summary>
    /// Gives every tracker of the group the group's display settings, load conditions and icon size.
    /// A group works like a template: what is set on the group overwrites the same setting in each of its trackers.
    /// </summary>
    public void ApplyGroupStyle(TrackerGroupRuntime group)
    {
        foreach (var member in MembersOf(group))
            ApplyGroupStyleTo(group, member);

        plugin.RequestSave();
    }

    private static void ApplyGroupStyleTo(TrackerGroupRuntime group, TrackerRuntime tracker)
    {
        if (group.Definition.Template is { } template)
            tracker.Definition.CopyStyleFrom(template);

        tracker.Element.Layout.Width = Math.Max(1, group.Definition.IconWidth);
        tracker.Element.Layout.Height = Math.Max(1, group.Definition.IconHeight);
    }

    private void PlaceAtRoot(string key, string targetKey, bool after)
    {
        var order = plugin.Configuration.RootOrder;
        order.Remove(key);

        var index = order.IndexOf(targetKey);
        if (index < 0)
            index = order.Count;
        else if (after)
            index++;

        order.Insert(index, key);
    }

    // Makes the top-level order match what exists: forgets what was deleted and adds what is missing.
    // Older configurations (which had no top-level order) get their groups first, then their loose trackers.
    private void ReconcileRootOrder()
    {
        var order = plugin.Configuration.RootOrder;

        var valid = new HashSet<string>();
        foreach (var group in groups)
            valid.Add(GroupKey(group.Definition));

        foreach (var tracker in runtimes)
        {
            if (tracker.Definition.GroupId is null)
                valid.Add(TrackerKey(tracker.Definition));
        }

        order.RemoveAll(key => !valid.Contains(key));

        var present = new HashSet<string>(order);

        foreach (var group in groups)
        {
            var key = GroupKey(group.Definition);
            if (present.Add(key))
                order.Add(key);
        }

        foreach (var tracker in runtimes)
        {
            if (tracker.Definition.GroupId is not null)
                continue;

            var key = TrackerKey(tracker.Definition);
            if (present.Add(key))
                order.Add(key);
        }
    }

    private TrackerRuntime Register(TrackerDefinition definition)
    {
        // New trackers start in the centre of the screen, so they can never end up off screen.
        var defaults = new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
            Width = 44,
            Height = 44
        };

        var element = plugin.Layout.Register($"tracker.{definition.Id}", definition.Name, defaults);
        var runtime = new TrackerRuntime(definition, element);
        runtimes.Add(runtime);
        return runtime;
    }

    private TrackerGroupRuntime RegisterGroup(TrackerGroupDefinition definition)
    {
        var defaults = new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
            Width = 44,
            Height = 44
        };

        var element = plugin.Layout.Register($"tracker-group.{definition.Id}", $"Group: {definition.Name}", defaults);
        var runtime = new TrackerGroupRuntime(definition, element);
        groups.Add(runtime);
        return runtime;
    }

    // ---------------------------------------------------------------- per frame

    // The snapshot of the frame being drawn: texts that read other statuses, abilities or the combo need it.
    private GameSnapshot? frameSnapshot;

    public void Draw()
    {
        var snapshot = plugin.GameData.Current;
        frameSnapshot = snapshot;
        var unlock = plugin.Layout.UnlockMode;

        // Tell the game data layer which ability cooldowns are needed. They arrive in the next snapshot.
        watchedActions.Clear();
        foreach (var runtime in runtimes)
        {
            var definition = runtime.Definition;
            if (!definition.Enabled || FindGroup(definition.GroupId) is { Definition.Enabled: false })
                continue;

            foreach (var rule in definition.Rules)
            {
                if (!rule.IsAbility || rule.ActionId == 0)
                    continue;

                watchedActions.Add(rule.ActionId);

                // A step that is not a button of its own (Blade of Faith) is shown by the game as another form of the first
                // action of its chain (Confiteor): that one is watched too.
                if (rule.Kind == ConditionKind.AbilityHighlighted && GameNames.ComboRoot(rule.ActionId) is var root && root != 0)
                    watchedActions.Add(root);
            }

            foreach (var rule in definition.Rules)
            {
                foreach (var text in rule.Texts)
                {
                    foreach (var source in text.Sources)
                    {
                        if (source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges && source.ActionId != 0)
                            watchedActions.Add(source.ActionId);
                    }
                }
            }
        }

        plugin.GameData.SetWatchedActions(watchedActions);

        foreach (var group in groups)
        {
            group.Members.Clear();
            group.Element.Name = $"Group: {(string.IsNullOrWhiteSpace(group.Definition.Name) ? "unnamed" : group.Definition.Name)}";
        }

        // The job of the player, to leave the trackers of other jobs out of unlock mode (null while unknown: show all).
        uint? currentJob = snapshot.IsLoggedIn ? snapshot.Player?.JobId : null;

        // 1. Evaluate every tracker and sort the enabled ones into their groups.
        foreach (var runtime in runtimes)
        {
            var definition = runtime.Definition;

            // Keep the name shown in unlock mode and in the Layout page up to date.
            runtime.Element.Name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed tracker" : definition.Name;

            var group = FindGroup(definition.GroupId);

            // A tracker inside a group has no position of its own: the group places it.
            runtime.Element.IsManaged = group is not null;
            runtime.Element.IsHidden = group is null && !AppliesToJob(definition, currentJob, snapshot.Context.InPvP);

            if (!definition.Enabled || group is { Definition.Enabled: false } || runtime.ConsecutiveErrors >= MaxConsecutiveErrors)
            {
                runtime.State = TrackerState.Hidden;
                continue;
            }

            group?.Members.Add(runtime);

            // One broken tracker must never break the others.
            try
            {
                runtime.State = Evaluate(definition, snapshot);
            }
            catch (Exception ex)
            {
                Fail(runtime, ex);
            }
        }

        // 2. Draw the trackers that have their own position.
        foreach (var runtime in runtimes)
        {
            if (runtime.Element.IsManaged || !runtime.State.Visible || !plugin.Layout.IsShown(runtime.Element))
                continue;

            var (pos, size) = LayoutManager.GetRect(runtime.Element.Layout);
            SafeDraw(runtime, pos, size, false);
        }

        // 3. Line up and draw the groups.
        foreach (var group in groups)
        {
            // A group whose trackers all belong to other jobs is not offered for positioning.
            var anyApplies = group.Members.Count == 0;
            foreach (var member in group.Members)
            {
                if (AppliesToJob(member.Definition, currentJob, snapshot.Context.InPvP))
                    anyApplies = true;
            }

            group.Element.IsHidden = !anyApplies;

            // In unlock mode every enabled member is drawn as a ghost, so the group can be placed
            // even when nothing is active. Otherwise only the visible trackers take a slot.
            shown.Clear();
            foreach (var member in group.Members)
            {
                if ((unlock && AppliesToJob(member.Definition, currentJob, snapshot.Context.InPvP)) || member.State.Visible)
                    shown.Add(member);
            }

            // The group has its own "show when" conditions (all its trackers follow them).
            if (shown.Count == 0 || !plugin.Layout.IsShown(group.Element))
            {
                group.Element.DisplayRect = null;
                continue;
            }

            // The box of the group is the size of one of its icons (set in the Trackers page), so the group sits exactly where the Layout says.
            group.Element.Layout.Width = Math.Max(1, group.Definition.IconWidth);
            group.Element.Layout.Height = Math.Max(1, group.Definition.IconHeight);

            var (anchorPos, anchorSize) = LayoutManager.GetRect(group.Element.Layout);

            var sizes = new Vector2[shown.Count];
            for (var i = 0; i < shown.Count; i++)
                sizes[i] = LayoutManager.GetRect(shown[i].Element.Layout).Size;

            var positions = Arrange(group.Definition, anchorPos, anchorSize, sizes);

            // The box unlock mode shows for the group is the whole area its icons cover.
            var boxMin = positions[0];
            var boxMax = positions[0] + sizes[0];
            for (var i = 1; i < shown.Count; i++)
            {
                boxMin = Vector2.Min(boxMin, positions[i]);
                boxMax = Vector2.Max(boxMax, positions[i] + sizes[i]);
            }

            group.Element.DisplayRect = (boxMin, boxMax - boxMin);

            for (var i = 0; i < shown.Count; i++)
                SafeDraw(shown[i], positions[i], sizes[i], unlock);
        }
    }

    private void SafeDraw(TrackerRuntime runtime, Vector2 pos, Vector2 size, bool ghost)
    {
        try
        {
            plugin.GameCover.Draw(pos, size, 80f * ImGuiHelpers.GlobalScale, () => DrawTracker(runtime, pos, size, ghost));
            runtime.ConsecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            Fail(runtime, ex);
        }
    }

    private static void Fail(TrackerRuntime runtime, Exception ex)
    {
        runtime.State = TrackerState.Hidden;
        runtime.ConsecutiveErrors++;

        var name = runtime.Definition.Name;

        if (runtime.ConsecutiveErrors == 1)
            Plugin.Log.Error(ex, "NahjaUI: tracker {Name} failed.", name);

        if (runtime.ConsecutiveErrors == MaxConsecutiveErrors)
            Plugin.Log.Warning("NahjaUI: tracker {Name} was switched off after repeated errors.", name);
    }

    // ---------------------------------------------------------------- group layout

    /// <summary>
    /// Works out where each icon of a group goes, in screen pixels.
    /// The group's box is the reference: the row starts at its left or right edge, or is centred on it.
    /// Icons are centred on the box across the other direction.
    /// </summary>
    private static Vector2[] Arrange(TrackerGroupDefinition group, Vector2 anchorPos, Vector2 anchorSize, IReadOnlyList<Vector2> sizes)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = MathF.Round(Math.Max(0, group.Spacing) * scale);
        var count = sizes.Count;
        var positions = new Vector2[count];

        if (count == 0)
            return positions;

        var center = anchorPos + anchorSize * 0.5f;

        var horizontal = group.Growth is GroupGrowth.ToRight or GroupGrowth.ToLeft or GroupGrowth.FromCenterHorizontal;
        var reversed = group.Growth is GroupGrowth.ToLeft or GroupGrowth.Up;

        // Total length of the row (or column), gaps included.
        var total = gap * (count - 1);
        foreach (var size in sizes)
            total += horizontal ? size.X : size.Y;

        // Where the whole row starts along its direction.
        var start = group.Growth switch
        {
            GroupGrowth.ToRight => anchorPos.X,
            GroupGrowth.ToLeft => anchorPos.X + anchorSize.X - total,
            GroupGrowth.FromCenterHorizontal => center.X - total / 2f,
            GroupGrowth.Down => anchorPos.Y,
            GroupGrowth.Up => anchorPos.Y + anchorSize.Y - total,
            _ => center.Y - total / 2f
        };

        // Icons going left or up start from the far end, so the first tracker stays next to the box.
        var cursor = reversed ? start + total : start;

        for (var i = 0; i < count; i++)
        {
            var length = horizontal ? sizes[i].X : sizes[i].Y;
            float along;

            if (reversed)
            {
                cursor -= length;
                along = cursor;
                cursor -= gap;
            }
            else
            {
                along = cursor;
                cursor += length + gap;
            }

            var position = horizontal
                ? new Vector2(along, center.Y - sizes[i].Y / 2f)
                : new Vector2(center.X - sizes[i].X / 2f, along);

            positions[i] = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        }

        return positions;
    }

    // ---------------------------------------------------------------- evaluation

    private static TrackerState Evaluate(TrackerDefinition tracker, GameSnapshot snapshot)
    {
        var player = snapshot.Player;
        if (!snapshot.IsLoggedIn || player is null)
            return TrackerState.Hidden;

        if (!IsLoaded(tracker.Load, player, snapshot.Context))
            return TrackerState.Hidden;

        // The triggers are tried in order; the first one that matches decides. None matching hides the tracker.
        foreach (var rule in tracker.Rules)
        {
            // An ability not learned yet (level too low, or the level is synced down in a dungeon or FATE) never matches.
            // The player's level is the one the game uses, so it already includes the sync.
            // The role actions are the exception: once learned, the game keeps them when the level is synced down.
            if (rule.IsAbility && rule.ActionId != 0 && GameNames.ActionLevel(rule.ActionId) > player.Level && !GameNames.IsRoleAction(rule.ActionId))
                continue;

            var state = Match(rule, snapshot, player);
            if (state.Visible)
                return state;
        }

        return TrackerState.Hidden;
    }

    private static TrackerState Match(TrackerRule rule, GameSnapshot snapshot, UnitSnapshot player) => rule.Kind switch
    {
        ConditionKind.StatusPresent or ConditionKind.StatusMissing => MatchStatus(rule, snapshot, player),
        ConditionKind.AbilityHighlighted => MatchActivated(rule, snapshot),
        _ => MatchCooldown(rule, snapshot)
    };

    // An ability the game turns on right now: it glows, or the button of the first action of its chain has changed into it.
    private static TrackerState MatchActivated(TrackerRule rule, GameSnapshot snapshot)
    {
        if (rule.ActionId == 0 || !snapshot.Cooldowns.TryGetValue(rule.ActionId, out var cooldown))
            return TrackerState.Hidden;

        var root = GameNames.ComboRoot(rule.ActionId);
        bool offered;

        if (root != 0)
        {
            // A combo step: the game's combo is on the step before it, or (Confiteor into Blade of Faith) the button of the
            // first action of the chain is now this step.
            var parent = GameNames.ComboParent(rule.ActionId);
            var combo = snapshot.Combo;
            var byCombo = parent != 0 && combo is not null && combo.Action == parent && combo.Timer > 0f;
            var byForm = snapshot.Cooldowns.TryGetValue(root, out var rootCooldown) && rootCooldown.CurrentActionId == rule.ActionId;
            offered = byCombo || byForm;
        }
        else
        {
            // The glow of the game is on the current form of the ability.
            offered = cooldown.Highlighted;
        }

        if (!offered)
            return TrackerState.Hidden;

        // Standard number: the cooldown of the ability (the global cooldown does not count).
        var remaining = cooldown.IsOnCooldown && !(cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds) ? cooldown.Remaining : 0f;

        return new TrackerState(true, true, remaining, 0, TrackerPhase.Active, cooldown.Total, cooldown.Charges, cooldown.MaxCharges,
            cooldown.CurrentActionId, cooldown.Highlighted, true, rule);
    }

    private static TrackerState MatchStatus(TrackerRule rule, GameSnapshot snapshot, UnitSnapshot player)
    {
        if (rule.StatusId == 0)
            return TrackerState.Hidden;

        // Nobody to look at (for example no target): nothing matches, not even a "missing" reminder.
        if (!TryFindStatus(rule, snapshot, player, out var found))
            return TrackerState.Hidden;

        var present = found is not null;
        var wantPresent = rule.Kind == ConditionKind.StatusPresent;

        if (present != wantPresent)
            return TrackerState.Hidden;

        return new TrackerState(true, present, found?.RemainingTime ?? 0f, found?.Param ?? 0, Rule: rule, FoundStatusId: found?.Id ?? 0);
    }

    private static TrackerState MatchCooldown(TrackerRule rule, GameSnapshot snapshot)
    {
        // No ability chosen, or the data has not arrived yet (it comes one frame after the tracker asks for it).
        if (rule.ActionId == 0 || !snapshot.Cooldowns.TryGetValue(rule.ActionId, out var cooldown))
            return TrackerState.Hidden;

        var onCooldown = cooldown.IsOnCooldown;
        var remaining = cooldown.Remaining;

        // The global cooldown is not a real cooldown: abilities used with it count as off cooldown.
        if (cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds)
        {
            onCooldown = false;
            remaining = 0f;
        }

        // An ability with several charges where some are recharging: not full (it can still be used).
        var recharging = cooldown.MaxCharges > 1 && cooldown.Charges < cooldown.MaxCharges;

        var matches = rule.Kind switch
        {
            ConditionKind.AbilityOnCooldown => onCooldown,
            ConditionKind.AbilityOffCooldown => !onCooldown,
            ConditionKind.AbilityFull => !onCooldown && !recharging,
            ConditionKind.AbilityNotFull => recharging,
            _ => false
        };

        if (!matches)
            return TrackerState.Hidden;

        // Cooling: the sweep and the time are shown while something is recharging.
        var phase = onCooldown || recharging ? TrackerPhase.Cooling : TrackerPhase.Ready;

        return new TrackerState(true, false, remaining, 0, phase, cooldown.Total, cooldown.Charges, cooldown.MaxCharges,
            cooldown.CurrentActionId, cooldown.Highlighted, true, rule);
    }

    // Looks for the status of the trigger on the unit it watches.
    // Returns false when there is nobody to look at; "found" is null when the unit does not have the status.
    private static bool TryFindStatus(TrackerRule rule, GameSnapshot snapshot, UnitSnapshot player, out StatusSnapshot? found)
    {
        found = null;

        var unit = rule.Unit switch
        {
            TrackerUnit.Player => player,
            TrackerUnit.Target => snapshot.Target,
            TrackerUnit.Focus => snapshot.Focus,
            TrackerUnit.TargetOfTarget => snapshot.TargetOfTarget,
            _ => null
        };

        if (unit is null)
            return false;

        foreach (var status in unit.Statuses)
        {
            if (status.Id != rule.StatusId)
                continue;

            if (rule.OnlyMine && status.SourceId != player.EntityId)
                continue;

            found = status;
            break;
        }

        return true;
    }

    // Does the tracker load on this job and in this kind of content (PvE or PvP)? (Only those parts of the load conditions; when it is shown, combat and weapon, is the "show when" of the layout element.)
    private static bool AppliesToJob(TrackerDefinition tracker, uint? job, bool inPvp)
    {
        if (tracker.Load.Where == TrackerPvpMode.PveOnly && inPvp)
            return false;

        if (tracker.Load.Where == TrackerPvpMode.PvpOnly && !inPvp)
            return false;

        return job is null || tracker.Load.JobIds.Count == 0 || tracker.Load.JobIds.Contains(job.Value);
    }

    private static bool IsLoaded(TrackerLoadConditions load, UnitSnapshot player, ContextSnapshot context)
    {
        if (load.JobIds.Count > 0 && !load.JobIds.Contains(player.JobId))
            return false;

        if (load.Where == TrackerPvpMode.PveOnly && context.InPvP)
            return false;

        return !(load.Where == TrackerPvpMode.PvpOnly && !context.InPvP);
    }

    // ---------------------------------------------------------------- drawing

    // Status icons are portrait (24 x 32), ability icons are square. Their corners are rounded inside the image,
    // so the edge is cropped away.
    internal const float StatusIconAspect = 24f / 32f;

    /// <summary>
    /// Draws one tracker icon. A ghost is a faded preview (no text) used in unlock mode to show
    /// where the members of a group will be.
    /// </summary>
    private void DrawTracker(TrackerRuntime runtime, Vector2 pos, Vector2 size, bool ghost)
    {
        var definition = runtime.Definition;
        var state = runtime.State;
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetBackgroundDrawList();

        var isCooldown = state.AbilityBased || (state.Rule ?? definition.Rules.FirstOrDefault())?.IsAbility == true;

        // Rounded corners, like the HUD bars. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(GlobalColors.Current.Rounding * scale), MathF.Min(size.X, size.Y) / 2f);

        var cooling = !ghost && isCooldown && state.Phase == TrackerPhase.Cooling;
        var activeRule = state.Rule;
        var red = !ghost && activeRule is not null && activeRule.Red;
        var dim = !ghost && activeRule is not null && activeRule.Dim;

        // Cooldown trackers show the ability's own icon, status trackers the icon of the status.
        var iconId = ResolveIcon(state.Rule ?? definition.Rules.FirstOrDefault(), state.IconActionId, out var iconIsStatus, state.FoundStatusId);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

            // The icon is cropped (never stretched) to the shape of the box.
            // The empty margins of the game's icon are always cut so the art fills the box.
            var margins = IconMargins.Get(iconId);
            var (uv0, uv1) = IconUv(size, iconIsStatus ? StatusIconAspect : 1f, margins.Item1, margins.Item2, margins.Item3, margins.Item4);

            // A ghost is transparent. The effects of the trigger that decides: a red mask and/or darkened.
            var tint = Vector4.One;

            if (ghost)
            {
                tint = new Vector4(1f, 1f, 1f, 0.55f);
            }
            else
            {
                if (red)
                    tint *= new Vector4(1f, 0.45f, 0.45f, 1f);

                if (dim)
                    tint *= new Vector4(0.45f, 0.45f, 0.45f, 1f);
            }

            drawList.AddImageRounded(texture.Handle, pos, pos + size, uv0, uv1, ImGui.GetColorU32(tint), rounding, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.WithAlpha(Theme.Card, 0.9f)), rounding);
        }

        // The dark sweep that covers the part of the icon that is still recharging.
        if (cooling && definition.ShowCooldownSweep && state.Total > 0f)
            DrawSweep(drawList, pos, size, state.Remaining / state.Total, rounding);

        // 1 px border (it grows with the UI scale so it always looks 1 px thick):
        // black, accent colour for a ghost.
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var border = ghost ? Theme.Accent : new Vector4(0f, 0f, 0f, 1f);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, thickness);

        if (ghost)
            return;

        // Glow around the icon, as the trigger that decides asks for.
        var rule = state.Rule;
        if (rule is not null && rule.Glow)
        {
            DrawGlow(drawList, pos, size, rounding);
        }

        // The texts over the icon: the ones of the trigger that decides. Every text has its own source, size (height of the letters, in logical pixels: the fonts are
        // already built scaled by the user's UI scale, so it is not multiplied again), position and colour.
        foreach (var text in rule?.Texts ?? new List<TrackerTextDefinition>())
        {
            // The first source with a value wins.
            string? value = null;
            foreach (var source in text.Sources)
            {
                value = SourceValue(source, state);
                if (value is not null)
                    break;
            }

            if (value is null)
                continue;

            DrawOverlayText(drawList, value, Math.Clamp(text.Size, 4, 64), OverlayFont.NahjaDisplayBold, pos, size,
                text.Anchor, new Vector2(text.OffsetX, text.OffsetY), text.Color);
        }
    }

    // What one source of a text gives right now, or null when it has nothing to show (no time left, no stacks...).
    private string? SourceValue(TrackerTextSource text, TrackerState state)
    {
        var snapshot = frameSnapshot;
        var player = snapshot?.Player;

        switch (text.Source)
        {
            case TextSource.OwnTime:
                return state.Remaining > 0f && (state.AbilityBased || state.StatusPresent) ? FormatTime(state.Remaining) : null;

            case TextSource.OwnStacks:
                if (state.AbilityBased)
                    return state.MaxCharges > 1 ? state.Charges.ToString() : null;

                // Only statuses that really stack show a number. (For other statuses the game stores something else
                // there, for example the +30 % of Sprint.)
                return state.StatusPresent ? StackText(state.Rule?.StatusId ?? 0, state.Stacks) : null;

            case TextSource.ComboTime:
                return snapshot?.Combo is { Timer: > 0f } combo ? FormatTime(combo.Timer) : null;

            case TextSource.StatusTime:
            case TextSource.StatusStacks:
                if (player is null || text.StatusId == 0)
                    return null;

                foreach (var status in player.Statuses)
                {
                    if (status.Id != text.StatusId)
                        continue;

                    return text.Source == TextSource.StatusTime
                        ? status.RemainingTime > 0f ? FormatTime(status.RemainingTime) : null
                        : StackText(text.StatusId, status.Param);
                }

                return null;

            case TextSource.AbilityCooldown:
            case TextSource.AbilityCharges:
                if (snapshot is null || !snapshot.Cooldowns.TryGetValue(text.ActionId, out var cooldown))
                    return null;

                if (text.Source == TextSource.AbilityCharges)
                    return cooldown.MaxCharges > 1 ? cooldown.Charges.ToString() : null;

                // The global cooldown is not a real cooldown.
                if (!cooldown.IsOnCooldown || (cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds))
                    return null;

                return cooldown.Remaining > 0f ? FormatTime(cooldown.Remaining) : null;
        }

        return null;
    }

    // The stack count of a status as text, only for statuses that really stack.
    private static string? StackText(uint statusId, int stacks)
    {
        var maxStacks = GameNames.MaxStacks(statusId);
        return maxStacks > 1 && stacks > 1 && stacks <= maxStacks ? stacks.ToString() : null;
    }

    /// <summary>
    /// The game icon of a tracker: the one chosen by the user, or the one of what it tracks. "isStatusIcon" tells the shape
    /// (status icons are taller than ability icons). <paramref name="currentForm"/> is the form the game shows for an ability now (0 = the chosen one).
    /// </summary>
    internal static uint ResolveIcon(TrackerRule? rule, uint currentForm, out bool isStatusIcon, uint foundStatusId = 0)
    {
        isStatusIcon = false;

        if (rule is null)
            return 0;

        if (rule.Icon == IconSource.Status && rule.IconStatusId != 0)
        {
            isStatusIcon = true;
            return GameNames.StatusIconId(rule.IconStatusId);
        }

        if (rule.Icon == IconSource.Ability && rule.IconActionId != 0)
            return GameNames.ActionIconId(rule.IconActionId);

        isStatusIcon = !rule.IsAbility;
        return rule.IsAbility
            ? GameNames.ActionIconId(currentForm != 0 ? currentForm : rule.ActionId)
            : GameNames.StatusIconId(rule.StatusId);
    }

    // A dark wedge over the part of the icon that is still recharging, like the game's own hotbar:
    // it starts at 12 o'clock and the icon is uncovered clockwise as time passes.
    // The wedge follows the rounded corners of the icon: it is a fan of triangles from the centre to the edge of the
    // rounded box (a rounded box is star-shaped from its centre, so a fan covers it exactly).
    private static void DrawSweep(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float fractionRemaining, float rounding)
    {
        var fraction = Math.Clamp(fractionRemaining, 0f, 1f);
        if (fraction <= 0f)
            return;

        var center = pos + size * 0.5f;
        var color = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f));

        var startAngle = -MathF.PI / 2f + MathF.Tau * (1f - fraction);
        var steps = Math.Max(2, (int)MathF.Ceiling(96f * fraction));

        var previous = center + EdgeDirection(startAngle, size, rounding);
        for (var i = 1; i <= steps; i++)
        {
            var angle = startAngle + MathF.Tau * fraction * i / steps;
            var point = center + EdgeDirection(angle, size, rounding);

            drawList.AddTriangleFilled(center, previous, point, color);
            previous = point;
        }
    }

    // The vector from the centre of a rounded box to its edge, in the given direction.
    // The edge is found by halving the distance a few times (the box is convex, so it always works).
    private static Vector2 EdgeDirection(float angle, Vector2 size, float rounding)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var half = size * 0.5f;
        var inner = new Vector2(MathF.Max(0f, half.X - rounding), MathF.Max(0f, half.Y - rounding));

        var low = 0f;
        var high = MathF.Sqrt(size.X * size.X + size.Y * size.Y);

        for (var i = 0; i < 14; i++)
        {
            var middle = (low + high) * 0.5f;
            var point = direction * middle;

            // Distance to a rounded box: negative or zero means inside.
            var q = new Vector2(MathF.Abs(point.X) - inner.X, MathF.Abs(point.Y) - inner.Y);
            var outside = new Vector2(MathF.Max(q.X, 0f), MathF.Max(q.Y, 0f)).Length();
            var distance = outside + MathF.Min(MathF.Max(q.X, q.Y), 0f) - rounding;

            if (distance <= 0f)
                low = middle;
            else
                high = middle;
        }

        return direction * low;
    }

    // Reused every frame (the interface is drawn from one thread), so the glow does not create garbage.
    private static readonly List<Vector2> GlowPath = new();
    private static readonly List<float> GlowLengths = new();
    private static readonly List<Vector2> GlowDash = new();

    // The glow of the game's hotbar: short golden dashes with gaps that run around the edge of the icon.
    // The dashes follow the rounded corners. Their size is in logical pixels, so it looks the same at any UI scale.
    private static void DrawGlow(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float rounding)
    {
        var scale = ImGuiHelpers.GlobalScale;

        // The path runs just outside the border of the icon.
        var grow = MathF.Max(1f, MathF.Round(scale));
        var min = pos - new Vector2(grow);
        var max = pos + size + new Vector2(grow);
        var radius = MathF.Min(rounding + grow, MathF.Min(max.X - min.X, max.Y - min.Y) / 2f);

        BuildRoundedPath(min, max, radius);

        var total = GlowLengths[^1];
        if (total < 8f)
            return;

        // About 9 logical pixels per dash and gap, adjusted so a whole number of them fits the perimeter.
        var count = Math.Max(4, (int)MathF.Round(total / (9f * scale)));
        var period = total / count;
        var dashLength = period * 0.55f;

        // They run clockwise, about 30 logical pixels per second.
        var offset = (float)(ImGui.GetTime() * 30.0 * scale) % period;

        var thickness = MathF.Max(2f, MathF.Round(2f * scale));
        var dark = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f));
        var gold = ImGui.GetColorU32(new Vector4(1f, 0.88f, 0.45f, 1f));

        // A dark line under each dash keeps it readable over any icon.
        for (var pass = 0; pass < 2; pass++)
        {
            var color = pass == 0 ? dark : gold;
            var width = pass == 0 ? thickness + 2f : thickness;

            for (var i = 0; i < count; i++)
            {
                var start = offset + i * period;
                AddPathPiece(drawList, start, start + dashLength, total, color, width);
            }
        }
    }

    // The outline of a rounded box as a closed list of points, starting at the top edge and going clockwise,
    // with the running length at each point.
    private static void BuildRoundedPath(Vector2 min, Vector2 max, float radius)
    {
        GlowPath.Clear();
        GlowLengths.Clear();

        const int arcSteps = 6;

        void Corner(Vector2 center, float startAngle)
        {
            for (var i = 0; i <= arcSteps; i++)
            {
                var angle = startAngle + MathF.PI / 2f * i / arcSteps;
                GlowPath.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
        }

        // Clockwise on the screen (y goes down): top-left, top-right, bottom-right, bottom-left corners.
        Corner(new Vector2(min.X + radius, min.Y + radius), MathF.PI);
        Corner(new Vector2(max.X - radius, min.Y + radius), MathF.PI * 1.5f);
        Corner(new Vector2(max.X - radius, max.Y - radius), 0f);
        Corner(new Vector2(min.X + radius, max.Y - radius), MathF.PI * 0.5f);

        // Close the shape.
        GlowPath.Add(GlowPath[0]);

        var run = 0f;
        GlowLengths.Add(0f);
        for (var i = 1; i < GlowPath.Count; i++)
        {
            run += Vector2.Distance(GlowPath[i - 1], GlowPath[i]);
            GlowLengths.Add(run);
        }
    }

    // Draws the part of the path between two distances. The path is a loop, so a piece can cross the end and continue at the start.
    private static void AddPathPiece(ImDrawListPtr drawList, float from, float to, float total, uint color, float width)
    {
        var length = to - from;
        from %= total;
        to = from + length;

        if (to <= total)
        {
            DrawPathInterval(drawList, from, to, color, width);
            return;
        }

        DrawPathInterval(drawList, from, total, color, width);
        DrawPathInterval(drawList, 0f, to - total, color, width);
    }

    private static void DrawPathInterval(ImDrawListPtr drawList, float from, float to, uint color, float width)
    {
        GlowDash.Clear();
        GlowDash.Add(PointAt(from));

        for (var i = 1; i < GlowPath.Count; i++)
        {
            if (GlowLengths[i] > from && GlowLengths[i] < to)
                GlowDash.Add(GlowPath[i]);
        }

        GlowDash.Add(PointAt(to));

        for (var i = 1; i < GlowDash.Count; i++)
            drawList.AddLine(GlowDash[i - 1], GlowDash[i], color, width);
    }

    // The point of the path at a given distance from its start.
    private static Vector2 PointAt(float distance)
    {
        for (var i = 1; i < GlowPath.Count; i++)
        {
            if (GlowLengths[i] >= distance)
            {
                var segment = GlowLengths[i] - GlowLengths[i - 1];
                var t = segment <= 0f ? 0f : (distance - GlowLengths[i - 1]) / segment;
                return Vector2.Lerp(GlowPath[i - 1], GlowPath[i], t);
            }
        }

        return GlowPath[^1];
    }

    // The same inset on every side (the progress bar and the status rows, which keep a plain crop).
    internal static (Vector2 Uv0, Vector2 Uv1) IconUv(Vector2 boxSize, float iconAspect, float inset) =>
        IconUv(boxSize, iconAspect, inset, inset, inset, inset);

    // The part of the icon texture to show so that it fills a box of the given shape without distortion.
    internal static (Vector2 Uv0, Vector2 Uv1) IconUv(Vector2 boxSize, float iconAspect, float left, float top, float right, float bottom)
    {
        // The margins that are cut away (fractions of the width / height of the icon).
        var u0 = left;
        var u1 = 1f - right;
        var v0 = top;
        var v1 = 1f - bottom;

        var boxAspect = boxSize.X / Math.Max(1f, boxSize.Y);

        // The shape of the part of the icon that is kept.
        iconAspect *= (u1 - u0) / Math.Max(0.01f, v1 - v0);

        if (boxAspect > iconAspect)
        {
            // The box is wider than the icon: keep the full width and crop the top and bottom.
            var half = (v1 - v0) * (iconAspect / boxAspect) * 0.5f;
            v0 = 0.5f - half;
            v1 = 0.5f + half;
        }
        else
        {
            // The box is narrower than the icon: keep the full height and crop the sides.
            var half = (u1 - u0) * (boxAspect / iconAspect) * 0.5f;
            u0 = 0.5f - half;
            u1 = 0.5f + half;
        }

        return (new Vector2(u0, v0), new Vector2(u1, v1));
    }

    // The timer as a single plain text: "17m" or "28" (whole seconds, rounded up). No spacing tricks.
    internal static string FormatTime(float seconds)
    {
        if (seconds >= 60f)
            return $"{(int)(seconds / 60f)}m";

        return ((int)MathF.Ceiling(seconds)).ToString();
    }

    // White text with a thick black outline, readable on any icon.
    // The text is attached to a point of the icon (the anchor): the same point of the text is placed on it,
    // so a right anchor right-justifies the text and a centre anchor centres it. The offset then moves it (in pixels).
    internal void DrawOverlayText(ImDrawListPtr drawList, string text, float wantedPx, OverlayFont font, Vector2 boxPos, Vector2 boxSize, AnchorPoint anchor, Vector2 offset, Vector4? color = null)
    {
        var scale = ImGuiHelpers.GlobalScale;

        // The size is the height of the letters (not of the font's line box).
        using (var face = plugin.Fonts.PushCap(font, wantedPx))
        {
            var textSize = face.Measure(text);
            var fraction = anchor.Fraction();

            // Horizontally the same point of text and box meet; vertically the CENTRE of the letters goes on the anchor line.
            var pos = boxPos + boxSize * fraction - new Vector2(textSize.X * fraction.X, textSize.Y * 0.5f) + offset * scale;
            pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));

            face.Draw(drawList, pos, text, ImGui.GetColorU32(color ?? new Vector4(1f, 1f, 1f, 1f)));
        }
    }
}
