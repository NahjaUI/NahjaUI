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

namespace NahjaUI.Modules.Auras;

/// <summary>What a cooldown aura is doing right now.</summary>
internal enum AuraPhase
{
    /// <summary>Not a cooldown aura.</summary>
    None,

    /// <summary>The ability can be used.</summary>
    Ready,

    /// <summary>No charge is available: the ability is recharging.</summary>
    Cooling,

    /// <summary>The effect of the ability is on right now (for example a defensive buff).</summary>
    Active
}

/// <summary>The result of evaluating one aura for one frame.</summary>
/// <param name="Visible">Whether the icon is drawn.</param>
/// <param name="StatusPresent">Status auras: the status is there. Cooldown auras: the effect is active.</param>
/// <param name="Remaining">Seconds to show in the timer (status time, recharge time or effect time).</param>
/// <param name="Stacks">Status auras: stack count.</param>
/// <param name="Phase">Cooldown auras: ready, cooling or active.</param>
/// <param name="Total">Cooldown auras: seconds one charge takes to recharge (for the sweep).</param>
/// <param name="Charges">Cooldown auras: charges available.</param>
/// <param name="MaxCharges">Cooldown auras: charges the ability can hold.</param>
internal readonly record struct AuraState(
    bool Visible,
    bool StatusPresent,
    float Remaining,
    int Stacks,
    AuraPhase Phase = AuraPhase.None,
    float Total = 0f,
    int Charges = 0,
    int MaxCharges = 1)
{
    public static readonly AuraState Hidden = new(false, false, 0f, 0);
}

/// <summary>An aura together with its layout element and its latest state.</summary>
internal sealed class AuraRuntime
{
    public AuraRuntime(AuraDefinition definition, LayoutElement element)
    {
        Definition = definition;
        Element = element;
    }

    public AuraDefinition Definition { get; }

    /// <summary>Holds the size of the icon, and its position when it is not inside a group.</summary>
    public LayoutElement Element { get; }

    public AuraState State { get; set; } = AuraState.Hidden;
    public int ConsecutiveErrors { get; set; }
}

/// <summary>A group together with its layout element (the box that is moved in unlock mode).</summary>
internal sealed class AuraGroupRuntime
{
    public AuraGroupRuntime(AuraGroupDefinition definition, LayoutElement element)
    {
        Definition = definition;
        Element = element;
    }

    public AuraGroupDefinition Definition { get; }
    public LayoutElement Element { get; }

    /// <summary>The enabled auras of this group, in display order. Rebuilt every frame.</summary>
    public List<AuraRuntime> Members { get; } = new();
}

/// <summary>
/// Every frame: evaluate each enabled aura against the game snapshot, line up the auras of each group,
/// and draw the visible icons. It only reads the snapshot and never calls Dalamud game services directly.
/// </summary>
internal sealed class AuraEngine
{
    // An aura that throws this many errors in a row is switched off until the plugin is reloaded.
    private const int MaxConsecutiveErrors = 5;

    // A cooldown up to this many seconds is the global cooldown, not a real cooldown.
    private const float GlobalCooldownSeconds = 3.5f;

    private readonly Plugin plugin;
    private readonly List<AuraRuntime> runtimes = new();
    private readonly List<AuraGroupRuntime> groups = new();
    private readonly List<AuraRuntime> shown = new();
    private readonly HashSet<uint> watchedActions = new();

    public AuraEngine(Plugin plugin)
    {
        this.plugin = plugin;
        LoadFromConfiguration();
    }

    /// <summary>
    /// Forgets every aura and group in memory and builds them again from the configuration. Used after a profile is
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
        foreach (var definition in plugin.Configuration.AuraGroups)
            RegisterGroup(definition);

        foreach (var definition in plugin.Configuration.Auras)
        {
            // Forget a group that no longer exists.
            if (definition.GroupId is not null && FindGroup(definition.GroupId) is null)
                definition.GroupId = null;

            Register(definition);
        }

        // Groups made before group sizes and group styles existed take them from their first aura.
        foreach (var group in groups)
        {
            var definition = group.Definition;
            var first = MembersOf(group).FirstOrDefault();

            if (definition.IconWidth <= 0 || definition.IconHeight <= 0)
            {
                definition.IconWidth = first?.Element.Layout.Width ?? 44;
                definition.IconHeight = first?.Element.Layout.Height ?? 44;
            }

            if (definition.Template is null)
            {
                definition.Template = new AuraDefinition();

                if (first is not null)
                    definition.Template.CopyStyleFrom(first.Definition);
            }
        }

        ReconcileRootOrder();
    }

    public IReadOnlyList<AuraRuntime> Auras => runtimes;
    public IReadOnlyList<AuraGroupRuntime> Groups => groups;

    public AuraGroupRuntime? FindGroup(string? id)
    {
        return id is null ? null : groups.FirstOrDefault(g => g.Definition.Id == id);
    }

    // ---------------------------------------------------------------- creating and removing

    /// <summary>
    /// Creates a new, empty aura. It goes into the given group, or to the end of the top level of the list.
    /// </summary>
    public AuraRuntime AddNew(AuraGroupRuntime? intoGroup = null)
    {
        var definition = new AuraDefinition();
        plugin.Configuration.Auras.Add(definition);

        var runtime = Register(definition);

        if (intoGroup is null)
            plugin.Configuration.RootOrder.Add(AuraKey(definition));
        else
            MoveAuraIntoGroup(runtime, intoGroup, null, false);

        plugin.RequestSave();
        return runtime;
    }

    public void Remove(AuraRuntime runtime)
    {
        plugin.Configuration.Auras.Remove(runtime.Definition);
        plugin.Configuration.RootOrder.Remove(AuraKey(runtime.Definition));
        runtimes.Remove(runtime);
        plugin.Layout.Unregister(runtime.Element.Id);
        plugin.RequestSave();
    }

    public AuraGroupRuntime AddGroup()
    {
        var definition = new AuraGroupDefinition { IconWidth = 44, IconHeight = 44, Template = new AuraDefinition() };
        plugin.Configuration.AuraGroups.Add(definition);
        plugin.Configuration.RootOrder.Add(GroupKey(definition));

        var runtime = RegisterGroup(definition);
        plugin.RequestSave();
        return runtime;
    }

    /// <summary>Deletes a group. Its auras are kept: they take the place of the group at the top level.</summary>
    public void RemoveGroup(AuraGroupRuntime group)
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
            order.Insert(Math.Min(index, order.Count), AuraKey(member.Definition));
            index++;
        }

        plugin.Configuration.AuraGroups.Remove(group.Definition);
        groups.Remove(group);
        plugin.Layout.Unregister(group.Element.Id);
        plugin.RequestSave();
    }

    // ---------------------------------------------------------------- the explorer tree

    public static string AuraKey(AuraDefinition definition) => $"aura:{definition.Id}";

    public static string GroupKey(AuraGroupDefinition definition) => $"group:{definition.Id}";

    /// <summary>One entry at the top level of the list: a group or a loose aura.</summary>
    internal readonly record struct RootEntry(AuraGroupRuntime? Group, AuraRuntime? Aura);

    /// <summary>The top level of the list in the order the user chose, with groups and loose auras mixed.</summary>
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
            else if (key.StartsWith("aura:", StringComparison.Ordinal))
            {
                var id = key["aura:".Length..];
                var aura = runtimes.FirstOrDefault(r => r.Definition.Id == id);

                if (aura is not null && aura.Definition.GroupId is null)
                    result.Add(new RootEntry(null, aura));
            }
        }

        return result;
    }

    /// <summary>The auras of a group, in display order.</summary>
    internal List<AuraRuntime> MembersOf(AuraGroupRuntime group)
    {
        return runtimes.Where(r => r.Definition.GroupId == group.Definition.Id).ToList();
    }

    /// <summary>Moves a group to the top level, before or after another top-level entry.</summary>
    public void MoveGroup(AuraGroupRuntime group, string targetKey, bool after)
    {
        PlaceAtRoot(GroupKey(group.Definition), targetKey, after);
        plugin.RequestSave();
    }

    /// <summary>
    /// Takes an aura out of its group (if it was in one) and puts it at the top level,
    /// before or after another top-level entry.
    /// </summary>
    public void MoveAuraToRoot(AuraRuntime aura, string targetKey, bool after)
    {
        aura.Definition.GroupId = null;
        PlaceAtRoot(AuraKey(aura.Definition), targetKey, after);
        plugin.RequestSave();
    }

    /// <summary>
    /// Puts an aura inside a group, before or after one of the group's auras
    /// (at the end of the group when no neighbour is given). An aura that joins a group takes the group's icon size.
    /// </summary>
    public void MoveAuraIntoGroup(AuraRuntime aura, AuraGroupRuntime group, AuraRuntime? neighbour, bool after)
    {
        if (neighbour == aura)
            return;

        var joining = aura.Definition.GroupId != group.Definition.Id;

        plugin.Configuration.RootOrder.Remove(AuraKey(aura.Definition));
        aura.Definition.GroupId = group.Definition.Id;

        // The order inside a group is the order of the aura list: take the aura out and put it back in the right place.
        var configList = plugin.Configuration.Auras;
        runtimes.Remove(aura);
        configList.Remove(aura.Definition);

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

        runtimes.Insert(index, aura);
        configList.Insert(index, aura.Definition);

        if (joining)
            ApplyGroupStyleTo(group, aura);

        plugin.RequestSave();
    }

    /// <summary>
    /// Gives every aura of the group the group's display settings, load conditions and icon size.
    /// A group works like a template: what is set on the group overwrites the same setting in each of its auras.
    /// </summary>
    public void ApplyGroupStyle(AuraGroupRuntime group)
    {
        foreach (var member in MembersOf(group))
            ApplyGroupStyleTo(group, member);

        plugin.RequestSave();
    }

    private static void ApplyGroupStyleTo(AuraGroupRuntime group, AuraRuntime aura)
    {
        if (group.Definition.Template is { } template)
            aura.Definition.CopyStyleFrom(template);

        aura.Element.Layout.Width = Math.Max(1, group.Definition.IconWidth);
        aura.Element.Layout.Height = Math.Max(1, group.Definition.IconHeight);
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
    // Older configurations (which had no top-level order) get their groups first, then their loose auras.
    private void ReconcileRootOrder()
    {
        var order = plugin.Configuration.RootOrder;

        var valid = new HashSet<string>();
        foreach (var group in groups)
            valid.Add(GroupKey(group.Definition));

        foreach (var aura in runtimes)
        {
            if (aura.Definition.GroupId is null)
                valid.Add(AuraKey(aura.Definition));
        }

        order.RemoveAll(key => !valid.Contains(key));

        var present = new HashSet<string>(order);

        foreach (var group in groups)
        {
            var key = GroupKey(group.Definition);
            if (present.Add(key))
                order.Add(key);
        }

        foreach (var aura in runtimes)
        {
            if (aura.Definition.GroupId is not null)
                continue;

            var key = AuraKey(aura.Definition);
            if (present.Add(key))
                order.Add(key);
        }
    }

    private AuraRuntime Register(AuraDefinition definition)
    {
        // New auras are lined up in rows of 10 so they do not pile up on top of each other.
        var index = runtimes.Count;
        var defaults = new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = (index % 10) * 50 - 225,
            OffsetY = 120 + (index / 10) * 50,
            Width = 44,
            Height = 44
        };

        var element = plugin.Layout.Register($"aura.{definition.Id}", definition.Name, defaults);
        var runtime = new AuraRuntime(definition, element);
        runtimes.Add(runtime);
        return runtime;
    }

    private AuraGroupRuntime RegisterGroup(AuraGroupDefinition definition)
    {
        var defaults = new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 200 + groups.Count * 60,
            Width = 44,
            Height = 44
        };

        var element = plugin.Layout.Register($"aura-group.{definition.Id}", $"Group: {definition.Name}", defaults);
        var runtime = new AuraGroupRuntime(definition, element);
        groups.Add(runtime);
        return runtime;
    }

    // ---------------------------------------------------------------- per frame

    public void Draw()
    {
        var snapshot = plugin.GameData.Current;
        var unlock = plugin.Layout.UnlockMode;

        // Tell the game data layer which ability cooldowns are needed. They arrive in the next snapshot.
        watchedActions.Clear();
        foreach (var runtime in runtimes)
        {
            var definition = runtime.Definition;
            if (definition.Enabled && definition.Trigger == AuraTriggerType.Cooldown && definition.ActionId != 0)
                watchedActions.Add(definition.ActionId);
        }

        plugin.GameData.SetWatchedActions(watchedActions);

        foreach (var group in groups)
        {
            group.Members.Clear();
            group.Element.Name = $"Group: {(string.IsNullOrWhiteSpace(group.Definition.Name) ? "unnamed" : group.Definition.Name)}";
        }

        // The job of the player, to leave the auras of other jobs out of unlock mode (null while unknown: show all).
        uint? currentJob = snapshot.IsLoggedIn ? snapshot.Player?.JobId : null;

        // 1. Evaluate every aura and sort the enabled ones into their groups.
        foreach (var runtime in runtimes)
        {
            var definition = runtime.Definition;

            // Keep the name shown in unlock mode and in the Layout page up to date.
            runtime.Element.Name = string.IsNullOrWhiteSpace(definition.Name) ? "Unnamed aura" : definition.Name;

            var group = FindGroup(definition.GroupId);

            // An aura inside a group has no position of its own: the group places it.
            runtime.Element.IsManaged = group is not null;
            runtime.Element.IsHidden = group is null && !AppliesToJob(definition, currentJob);

            if (!definition.Enabled || runtime.ConsecutiveErrors >= MaxConsecutiveErrors)
            {
                runtime.State = AuraState.Hidden;
                continue;
            }

            group?.Members.Add(runtime);

            // One broken aura must never break the others.
            try
            {
                runtime.State = Evaluate(definition, snapshot);
            }
            catch (Exception ex)
            {
                Fail(runtime, ex);
            }
        }

        // 2. Draw the auras that have their own position.
        foreach (var runtime in runtimes)
        {
            if (runtime.Element.IsManaged || !runtime.State.Visible)
                continue;

            var (pos, size) = LayoutManager.GetRect(runtime.Element.Layout);
            SafeDraw(runtime, pos, size, false);
        }

        // 3. Line up and draw the groups.
        foreach (var group in groups)
        {
            // A group whose auras all belong to other jobs is not offered for positioning.
            var anyApplies = group.Members.Count == 0;
            foreach (var member in group.Members)
            {
                if (AppliesToJob(member.Definition, currentJob))
                    anyApplies = true;
            }

            group.Element.IsHidden = !anyApplies;

            // In unlock mode every enabled member is drawn as a ghost, so the group can be placed
            // even when nothing is active. Otherwise only the visible auras take a slot.
            shown.Clear();
            foreach (var member in group.Members)
            {
                if ((unlock && AppliesToJob(member.Definition, currentJob)) || member.State.Visible)
                    shown.Add(member);
            }

            if (shown.Count == 0)
            {
                group.Element.DisplayRect = null;
                continue;
            }

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

    private void SafeDraw(AuraRuntime runtime, Vector2 pos, Vector2 size, bool ghost)
    {
        try
        {
            DrawAura(runtime, pos, size, ghost);
            runtime.ConsecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            Fail(runtime, ex);
        }
    }

    private static void Fail(AuraRuntime runtime, Exception ex)
    {
        runtime.State = AuraState.Hidden;
        runtime.ConsecutiveErrors++;

        var name = runtime.Definition.Name;

        if (runtime.ConsecutiveErrors == 1)
            Plugin.Log.Error(ex, "NahjaUI: aura {Name} failed.", name);

        if (runtime.ConsecutiveErrors == MaxConsecutiveErrors)
            Plugin.Log.Warning("NahjaUI: aura {Name} was switched off after repeated errors.", name);
    }

    // ---------------------------------------------------------------- group layout

    /// <summary>
    /// Works out where each icon of a group goes, in screen pixels.
    /// The group's box is the reference: the row starts at its left or right edge, or is centred on it.
    /// Icons are centred on the box across the other direction.
    /// </summary>
    private static Vector2[] Arrange(AuraGroupDefinition group, Vector2 anchorPos, Vector2 anchorSize, IReadOnlyList<Vector2> sizes)
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

        // Icons going left or up start from the far end, so the first aura stays next to the box.
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

    private static AuraState Evaluate(AuraDefinition aura, GameSnapshot snapshot)
    {
        var player = snapshot.Player;
        if (!snapshot.IsLoggedIn || player is null)
            return AuraState.Hidden;

        if (!IsLoaded(aura.Load, player, snapshot.Context))
            return AuraState.Hidden;

        return aura.Trigger == AuraTriggerType.Cooldown
            ? EvaluateCooldown(aura, snapshot, player)
            : EvaluateStatus(aura, snapshot, player);
    }

    private static AuraState EvaluateStatus(AuraDefinition aura, GameSnapshot snapshot, UnitSnapshot player)
    {
        if (aura.StatusId == 0)
            return AuraState.Hidden;

        // Nobody to look at (for example no target): show nothing, not even a "missing" reminder.
        if (!TryFindStatus(aura, snapshot, player, out var found))
            return AuraState.Hidden;

        var present = found is not null;
        var visible = aura.ShowMode == AuraShowMode.WhenPresent ? present : !present;

        if (!visible)
            return AuraState.Hidden;

        return new AuraState(true, present, found?.RemainingTime ?? 0f, found?.Param ?? 0);
    }

    private static AuraState EvaluateCooldown(AuraDefinition aura, GameSnapshot snapshot, UnitSnapshot player)
    {
        // No ability chosen, or the data has not arrived yet (it comes one frame after the aura asks for it).
        if (aura.ActionId == 0 || !snapshot.Cooldowns.TryGetValue(aura.ActionId, out var cooldown))
            return AuraState.Hidden;

        var onCooldown = cooldown.IsOnCooldown;
        var remaining = cooldown.Remaining;

        // The global cooldown is not a real cooldown: abilities used with it count as ready.
        if (aura.IgnoreGlobalCooldown && cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds)
        {
            onCooldown = false;
            remaining = 0f;
        }

        // The effect of the ability (optional): its remaining time is shown while it lasts.
        StatusSnapshot? effect = null;
        if (aura.StatusId != 0)
            TryFindStatus(aura, snapshot, player, out effect);

        var phase = effect is not null
            ? AuraPhase.Active
            : onCooldown ? AuraPhase.Cooling : AuraPhase.Ready;

        var visible = aura.CooldownShow switch
        {
            CooldownShowMode.OnlyOnCooldown => phase != AuraPhase.Ready,
            CooldownShowMode.OnlyWhenReady => phase == AuraPhase.Ready,
            _ => true
        };

        if (!visible)
            return AuraState.Hidden;

        var timer = phase == AuraPhase.Active ? effect?.RemainingTime ?? 0f : remaining;

        return new AuraState(true, effect is not null, timer, 0, phase, cooldown.Total, cooldown.Charges, cooldown.MaxCharges);
    }

    // Looks for the aura's status on the unit it watches.
    // Returns false when there is nobody to look at; "found" is null when the unit does not have the status.
    private static bool TryFindStatus(AuraDefinition aura, GameSnapshot snapshot, UnitSnapshot player, out StatusSnapshot? found)
    {
        found = null;

        var unit = aura.Unit switch
        {
            AuraUnit.Player => player,
            AuraUnit.Target => snapshot.Target,
            AuraUnit.Focus => snapshot.Focus,
            AuraUnit.TargetOfTarget => snapshot.TargetOfTarget,
            _ => null
        };

        if (unit is null)
            return false;

        foreach (var status in unit.Statuses)
        {
            if (status.Id != aura.StatusId)
                continue;

            if (aura.OnlyMine && status.SourceId != player.EntityId)
                continue;

            found = status;
            break;
        }

        return true;
    }

    // Does the aura load on this job? (Only the job part of the load conditions; combat does not matter for placing.)
    private static bool AppliesToJob(AuraDefinition aura, uint? job) =>
        job is null || aura.Load.JobIds.Count == 0 || aura.Load.JobIds.Contains(job.Value);

    private static bool IsLoaded(AuraLoadConditions load, UnitSnapshot player, ContextSnapshot context)
    {
        if (load.JobIds.Count > 0 && !load.JobIds.Contains(player.JobId))
            return false;

        return load.Combat switch
        {
            AuraCombatMode.InCombat => context.InCombat,
            AuraCombatMode.OutOfCombat => !context.InCombat,
            _ => true
        };
    }

    // ---------------------------------------------------------------- drawing

    // Status icons are portrait (24 x 32), ability icons are square. Their corners are rounded inside the image,
    // so the edge is cropped away.
    internal const float StatusIconAspect = 24f / 32f;

    private static readonly Vector4 GlowColor = new(1f, 0.82f, 0.30f, 1f);

    /// <summary>
    /// Draws one aura icon. A ghost is a faded preview (no text) used in unlock mode to show
    /// where the members of a group will be.
    /// </summary>
    private void DrawAura(AuraRuntime runtime, Vector2 pos, Vector2 size, bool ghost)
    {
        var definition = runtime.Definition;
        var state = runtime.State;
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetBackgroundDrawList();

        var isCooldown = definition.Trigger == AuraTriggerType.Cooldown;

        // Rounded corners, like the unit frames. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(Math.Clamp(definition.CornerRadiusPx, 0, 24) * scale), MathF.Min(size.X, size.Y) / 2f);

        var cooling = !ghost && isCooldown && state.Phase == AuraPhase.Cooling;
        var missing = !ghost && !isCooldown && !state.StatusPresent;
        var redBorder = missing || (cooling && definition.CooldownLook == CooldownLook.Red);

        // Cooldown auras show the ability's own icon, status auras the icon of the status.
        var iconId = isCooldown
            ? GameNames.ActionIconId(definition.ActionId)
            : GameNames.StatusIconId(definition.StatusId);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

            // The icon is cropped (never stretched) to the shape of the box.
            var crop = Math.Clamp(definition.IconCropPercent, 0, 30) / 100f;
            var (uv0, uv1) = IconUv(size, isCooldown ? 1f : StatusIconAspect, crop);

            // A ghost is transparent. A missing status is reddish. A recharging ability is dimmed or red.
            var tint = Vector4.One;

            if (ghost)
            {
                tint = new Vector4(1f, 1f, 1f, 0.55f);
            }
            else if (missing)
            {
                tint = new Vector4(1f, 0.55f, 0.55f, 1f);
            }
            else if (cooling)
            {
                tint = definition.CooldownLook switch
                {
                    CooldownLook.Dimmed => new Vector4(0.45f, 0.45f, 0.45f, 1f),
                    CooldownLook.Red => new Vector4(1f, 0.45f, 0.45f, 1f),
                    _ => Vector4.One
                };
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
        // black, red when the status is missing or the ability is recharging in red, accent colour for a ghost.
        var thickness = MathF.Max(1f, MathF.Round(scale));
        var border = ghost ? Theme.Accent : redBorder ? Theme.Error : new Vector4(0f, 0f, 0f, 1f);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, redBorder ? thickness * 2f : thickness);

        if (ghost)
            return;

        // Glow around the icon: when the ability is ready, or while its effect is active.
        if (isCooldown)
        {
            var glow = (state.Phase == AuraPhase.Ready && definition.GlowWhenReady)
                       || (state.Phase == AuraPhase.Active && definition.GlowWhenActive);

            if (glow)
                DrawGlow(drawList, pos, size, rounding);
        }

        // Text size is a font size in LOGICAL pixels, chosen per aura. Dalamud already builds the fonts scaled by the
        // user's UI scale, so it must NOT be multiplied by the scale again.
        var textPx = Math.Clamp(definition.TextSize, 8, 64);

        // The charges / stack count have their own size; 0 = automatic (three quarters of the timer size).
        var stackPx = definition.StackTextSize > 0 ? Math.Clamp(definition.StackTextSize, 8, 64) : textPx * 0.75f;

        var showTimer = definition.ShowTimer && state.Remaining > 0f && (isCooldown || state.StatusPresent);
        if (showTimer)
            DrawOverlayText(drawList, FormatTime(state.Remaining), textPx, definition.Font, pos, size,
                definition.TimerAnchor, new Vector2(definition.TimerOffsetX, definition.TimerOffsetY));

        if (isCooldown)
        {
            // Charges left, for abilities that have more than one.
            if (definition.ShowStacks && state.MaxCharges > 1)
                DrawOverlayText(drawList, state.Charges.ToString(), stackPx, definition.Font, pos, size,
                    definition.StacksAnchor, new Vector2(definition.StacksOffsetX, definition.StacksOffsetY));
        }
        else if (state.StatusPresent && definition.ShowStacks)
        {
            // Only statuses that really stack show a number. (For other statuses the game stores something else
            // there, for example the +30 % of Sprint.)
            var maxStacks = GameNames.MaxStacks(definition.StatusId);
            if (maxStacks > 1 && state.Stacks > 1 && state.Stacks <= maxStacks)
                DrawOverlayText(drawList, state.Stacks.ToString(), stackPx, definition.Font, pos, size,
                    definition.StacksAnchor, new Vector2(definition.StacksOffsetX, definition.StacksOffsetY));
        }
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

    // A gold glow that pulses about once a second: a few fading frames around the icon and a bright edge.
    private static void DrawGlow(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float rounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var pulse = 0.775f + 0.225f * MathF.Sin((float)(ImGui.GetTime() * MathF.Tau));

        const int layers = 4;
        for (var i = 1; i <= layers; i++)
        {
            var grow = new Vector2(i * 1.5f * scale);
            var alpha = pulse * (1f - (i - 1) / (float)layers) * 0.55f;

            drawList.AddRect(
                pos - grow,
                pos + size + grow,
                ImGui.GetColorU32(Theme.WithAlpha(GlowColor, alpha)),
                rounding > 0f ? rounding + grow.X : 0f,
                ImDrawFlags.None,
                1.5f * scale);
        }

        var edge = MathF.Max(1f, MathF.Round(scale)) + 1f;
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Theme.WithAlpha(GlowColor, pulse)), rounding, ImDrawFlags.None, edge);
    }

    // The part of the icon texture to show so that it fills a box of the given shape without distortion.
    internal static (Vector2 Uv0, Vector2 Uv1) IconUv(Vector2 boxSize, float iconAspect, float inset)
    {
        var u0 = inset;
        var u1 = 1f - inset;
        var v0 = inset;
        var v1 = 1f - inset;

        var boxAspect = boxSize.X / Math.Max(1f, boxSize.Y);

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

    // The timer as a single plain text: "17m", "28" or "6.5". No spacing tricks: the font is drawn as it is.
    internal static string FormatTime(float seconds)
    {
        if (seconds >= 60f)
            return $"{(int)(seconds / 60f)}m";

        if (seconds >= 10f)
            return ((int)MathF.Ceiling(seconds)).ToString();

        return seconds.ToString("0.0");
    }

    // White text with a thick black outline, readable on any icon.
    // The text is attached to a point of the icon (the anchor): the same point of the text is placed on it,
    // so a right anchor right-justifies the text and a centre anchor centres it. The offset then moves it (in pixels).
    internal void DrawOverlayText(ImDrawListPtr drawList, string text, float wantedPx, OverlayFont font, Vector2 boxPos, Vector2 boxSize, AnchorPoint anchor, Vector2 offset)
    {
        var scale = ImGuiHelpers.GlobalScale;

        using (plugin.Fonts.PushClosest(font, wantedPx))
        {
            var textSize = ImGui.CalcTextSize(text);
            var fraction = anchor.Fraction();

            var pos = boxPos + boxSize * fraction - textSize * fraction + offset * scale;
            pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));

            OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        }
    }
}
