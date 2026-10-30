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

    /// <summary>The effect of the ability is on right now (for example a defensive enhancement).</summary>
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
/// <param name="Highlighted">Cooldown auras: the game is making the button of the current form glow.</param>
/// <param name="AbilityBased">The trigger that matched works with an ability (not a status).</param>
/// <param name="Rule">The icon trigger that matched: it decides the picture and the glow.</param>
/// <param name="IconActionId">Cooldown auras: the action whose icon is shown (the form the game shows now; 0 = the chosen one).</param>
internal readonly record struct AuraState(
    bool Visible,
    bool StatusPresent,
    float Remaining,
    int Stacks,
    AuraPhase Phase = AuraPhase.None,
    float Total = 0f,
    int Charges = 0,
    int MaxCharges = 1,
    uint IconActionId = 0,
    bool Highlighted = false,
    bool AbilityBased = false,
    AuraRule? Rule = null)
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

    // Cooldown auras used to have an "effect while active". Now the time of that status is a text of the aura.
    private static void MigrateEffect(AuraDefinition aura)
    {
        if (aura.Trigger == AuraTriggerType.Cooldown && aura.StatusId != 0)
        {
            aura.Texts.Add(new AuraTextDefinition
            {
                Source = TextSource.StatusTime,
                StatusId = aura.StatusId,
                Anchor = AnchorPoint.TopRight
            });
        }

        if (aura.Trigger == AuraTriggerType.Cooldown)
        {
            aura.StatusId = 0;
            aura.ExtraEffectIds.Clear();
        }
    }

    // Auras saved before the text list existed: their timer and stack texts become texts of their own.
    private static void MigrateTexts(AuraDefinition aura)
    {
        if (aura.TextsMigrated)
            return;

        aura.TextsMigrated = true;
        aura.Texts.Clear();

        if (aura.ShowTimer)
        {
            aura.Texts.Add(new AuraTextDefinition
            {
                Source = TextSource.OwnTime,
                Font = aura.Font,
                Size = aura.TextSize,
                Anchor = aura.TimerAnchor,
                OffsetX = aura.TimerOffsetX,
                OffsetY = aura.TimerOffsetY
            });
        }

        if (aura.ShowStacks)
        {
            aura.Texts.Add(new AuraTextDefinition
            {
                Source = TextSource.OwnStacks,
                Font = aura.Font,
                Size = aura.StackTextSize > 0 ? aura.StackTextSize : 8,
                Anchor = aura.StacksAnchor,
                OffsetX = aura.StacksOffsetX,
                OffsetY = aura.StacksOffsetY
            });
        }
    }

    // Auras saved before the trigger list: what they watched becomes one trigger, and each text gets its first source.
    private static void MigrateRules(AuraDefinition aura)
    {
        foreach (var text in aura.Texts)
        {
            if (text.Sources.Count == 0)
                text.Sources.Add(new AuraTextSource { Source = text.Source, StatusId = text.StatusId, ActionId = text.ActionId });
        }

        if (aura.RulesMigrated)
            return;

        aura.RulesMigrated = true;
        aura.Rules.Clear();

        var rule = new AuraRule
        {
            Icon = aura.Icon,
            IconStatusId = aura.IconStatusId,
            IconActionId = aura.IconActionId
        };

        switch (aura.Trigger)
        {
            case AuraTriggerType.Cooldown:
                rule.Kind = aura.CooldownShow switch
                {
                    CooldownShowMode.OnlyOnCooldown => ConditionKind.AbilityOnCooldown,
                    CooldownShowMode.OnlyWhenReady => ConditionKind.AbilityReady,
                    _ => ConditionKind.AbilityAlways
                };
                rule.ActionId = aura.ActionId;
                rule.IgnoreGlobalCooldown = aura.IgnoreGlobalCooldown;
                rule.GlowWhenReady = aura.GlowWhenReady;
                rule.GlowWhenHighlighted = aura.GlowWhenHighlighted;
                break;

            case AuraTriggerType.Activated:
                rule.Kind = ConditionKind.AbilityActivated;
                rule.ActionId = aura.ActionId;
                rule.Glow = aura.GlowWhenActive;
                rule.GlowWhenHighlighted = aura.GlowWhenHighlighted;
                break;

            default:
                rule.Kind = aura.ShowMode == AuraShowMode.WhenMissing ? ConditionKind.StatusMissing : ConditionKind.StatusPresent;
                rule.Unit = aura.Unit;
                rule.StatusId = aura.StatusId;
                rule.OnlyMine = aura.OnlyMine;
                rule.Glow = aura.GlowWhenActive;
                break;
        }

        aura.Rules.Add(rule);
    }

    private void LoadFromConfiguration()
    {
        // Older auras that were "only as a combo step" are now activated-ability auras.
        foreach (var old in plugin.Configuration.Auras)
        {
            MigrateTexts(old);
            MigrateEffect(old);

            if (old.Trigger == AuraTriggerType.Cooldown && old.OnlyAsComboStep)
            {
                old.Trigger = AuraTriggerType.Activated;
                old.OnlyAsComboStep = false;
            }

            MigrateRules(old);
        }

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
        var definition = new AuraDefinition { TextsMigrated = true, RulesMigrated = true };
        definition.Rules.Add(new AuraRule());
        definition.Texts.Add(new AuraTextDefinition { Sources = { new AuraTextSource() } });
        plugin.Configuration.Auras.Add(definition);

        var runtime = Register(definition);

        if (intoGroup is null)
            plugin.Configuration.RootOrder.Add(AuraKey(definition));
        else
            MoveAuraIntoGroup(runtime, intoGroup, null, false);

        plugin.RequestSave();
        return runtime;
    }

    /// <summary>Makes a copy of an aura and puts it right below the original (inside the same group, or at the top level).</summary>
    public AuraRuntime Duplicate(AuraRuntime original)
    {
        // A deep copy through JSON: every setting of the aura, now and in the future, is copied without listing them here.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original.Definition);
        var definition = Newtonsoft.Json.JsonConvert.DeserializeObject<AuraDefinition>(json) ?? new AuraDefinition();
        definition.Id = Guid.NewGuid().ToString("N");
        definition.Name = string.IsNullOrWhiteSpace(original.Definition.Name) ? definition.Name : $"{original.Definition.Name} (copy)";

        plugin.Configuration.Auras.Add(definition);
        var runtime = Register(definition);

        // Same size and place as the original (a loose aura has a place of its own).
        runtime.Element.Layout.CopyPositionFrom(original.Element.Layout);
        runtime.Element.Layout.Width = original.Element.Layout.Width;
        runtime.Element.Layout.Height = original.Element.Layout.Height;

        var group = FindGroup(definition.GroupId);
        if (group is not null)
        {
            MoveAuraIntoGroup(runtime, group, original, true);
        }
        else
        {
            definition.GroupId = null;
            PlaceAtRoot(AuraKey(definition), AuraKey(original.Definition), true);
        }

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
        // New auras start in the centre of the screen, so they can never end up off screen.
        var defaults = new ElementLayout
        {
            ScreenAnchor = AnchorPoint.Center,
            SelfAnchor = AnchorPoint.Center,
            OffsetX = 0,
            OffsetY = 0,
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
            OffsetY = 0,
            Width = 44,
            Height = 44
        };

        var element = plugin.Layout.Register($"aura-group.{definition.Id}", $"Group: {definition.Name}", defaults);
        var runtime = new AuraGroupRuntime(definition, element);
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

        DecimalsBelowSeconds = plugin.Configuration.TimerDecimalsBelowSeconds;

        // Tell the game data layer which ability cooldowns are needed. They arrive in the next snapshot.
        watchedActions.Clear();
        foreach (var runtime in runtimes)
        {
            var definition = runtime.Definition;
            if (!definition.Enabled)
                continue;

            foreach (var rule in definition.Rules)
            {
                if (!rule.IsAbility || rule.ActionId == 0)
                    continue;

                watchedActions.Add(rule.ActionId);

                // A step that is not a button of its own (Blade of Faith) is shown by the game as another form of the first
                // action of its chain (Confiteor): that one is watched too.
                if (rule.Kind == ConditionKind.AbilityActivated && GameNames.ComboRoot(rule.ActionId) is var root && root != 0)
                    watchedActions.Add(root);
            }

            foreach (var text in definition.Texts)
            {
                foreach (var source in text.Sources)
                {
                    if (source.Source is TextSource.AbilityCooldown or TextSource.AbilityCharges && source.ActionId != 0)
                        watchedActions.Add(source.ActionId);
                }
            }
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

        // The triggers are tried in order; the first one that matches decides. None matching hides the aura.
        foreach (var rule in aura.Rules)
        {
            // An ability not learned yet (level too low, or the level is synced down in a dungeon or FATE) never matches.
            // The player's level is the one the game uses, so it already includes the sync.
            if (rule.IsAbility && rule.ActionId != 0 && GameNames.ActionLevel(rule.ActionId) > player.Level)
                continue;

            var state = Match(rule, snapshot, player);
            if (state.Visible)
                return state;
        }

        return AuraState.Hidden;
    }

    private static AuraState Match(AuraRule rule, GameSnapshot snapshot, UnitSnapshot player) => rule.Kind switch
    {
        ConditionKind.StatusPresent or ConditionKind.StatusMissing => MatchStatus(rule, snapshot, player),
        ConditionKind.AbilityActivated => MatchActivated(rule, snapshot),
        _ => MatchCooldown(rule, snapshot)
    };

    // An ability the game turns on right now: it glows, or the button of the first action of its chain has changed into it.
    private static AuraState MatchActivated(AuraRule rule, GameSnapshot snapshot)
    {
        if (rule.ActionId == 0 || !snapshot.Cooldowns.TryGetValue(rule.ActionId, out var cooldown))
            return AuraState.Hidden;

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
            return AuraState.Hidden;

        // Standard number: the cooldown of the ability (the global cooldown does not count).
        var remaining = cooldown.IsOnCooldown && !(cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds) ? cooldown.Remaining : 0f;

        return new AuraState(true, true, remaining, 0, AuraPhase.Active, cooldown.Total, cooldown.Charges, cooldown.MaxCharges,
            cooldown.CurrentActionId, cooldown.Highlighted, true, rule);
    }

    private static AuraState MatchStatus(AuraRule rule, GameSnapshot snapshot, UnitSnapshot player)
    {
        if (rule.StatusId == 0)
            return AuraState.Hidden;

        // Nobody to look at (for example no target): nothing matches, not even a "missing" reminder.
        if (!TryFindStatus(rule, snapshot, player, out var found))
            return AuraState.Hidden;

        var present = found is not null;
        var wantPresent = rule.Kind == ConditionKind.StatusPresent;

        if (present != wantPresent)
            return AuraState.Hidden;

        return new AuraState(true, present, found?.RemainingTime ?? 0f, found?.Param ?? 0, Rule: rule);
    }

    private static AuraState MatchCooldown(AuraRule rule, GameSnapshot snapshot)
    {
        // No ability chosen, or the data has not arrived yet (it comes one frame after the aura asks for it).
        if (rule.ActionId == 0 || !snapshot.Cooldowns.TryGetValue(rule.ActionId, out var cooldown))
            return AuraState.Hidden;

        var onCooldown = cooldown.IsOnCooldown;
        var remaining = cooldown.Remaining;

        // The global cooldown is not a real cooldown: abilities used with it count as ready.
        if (rule.IgnoreGlobalCooldown && cooldown.MaxCharges <= 1 && cooldown.Total <= GlobalCooldownSeconds)
        {
            onCooldown = false;
            remaining = 0f;
        }

        var phase = onCooldown ? AuraPhase.Cooling : AuraPhase.Ready;

        var matches = rule.Kind switch
        {
            ConditionKind.AbilityOnCooldown => phase == AuraPhase.Cooling,
            ConditionKind.AbilityReady => phase == AuraPhase.Ready,
            _ => true
        };

        if (!matches)
            return AuraState.Hidden;

        return new AuraState(true, false, remaining, 0, phase, cooldown.Total, cooldown.Charges, cooldown.MaxCharges,
            cooldown.CurrentActionId, cooldown.Highlighted, true, rule);
    }

    // Looks for the status of the trigger on the unit it watches.
    // Returns false when there is nobody to look at; "found" is null when the unit does not have the status.
    private static bool TryFindStatus(AuraRule rule, GameSnapshot snapshot, UnitSnapshot player, out StatusSnapshot? found)
    {
        found = null;

        var unit = rule.Unit switch
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
            if (status.Id != rule.StatusId)
                continue;

            if (rule.OnlyMine && status.SourceId != player.EntityId)
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

        var isCooldown = state.AbilityBased || (state.Rule ?? definition.Rules.FirstOrDefault())?.IsAbility == true;

        // Rounded corners, like the unit frames. They can never be more than half of the shorter side.
        var rounding = MathF.Min(MathF.Round(Math.Clamp(definition.CornerRadiusPx, 0, 24) * scale), MathF.Min(size.X, size.Y) / 2f);

        var cooling = !ghost && isCooldown && state.Phase == AuraPhase.Cooling;
        var missing = !ghost && !isCooldown && !state.StatusPresent;
        var redBorder = missing || (cooling && definition.CooldownLook == CooldownLook.Red);

        // Cooldown auras show the ability's own icon, status auras the icon of the status.
        var iconId = ResolveIcon(state.Rule ?? definition.Rules.FirstOrDefault(), state.IconActionId, out var iconIsStatus);

        if (iconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

            // The icon is cropped (never stretched) to the shape of the box.
            var crop = Math.Clamp(definition.IconCropPercent, 0, 30) / 100f;
            var (marginLeft, marginRight) = definition.FillWidth ? IconMargins.Get(iconId) : (0f, 0f);
            var (uv0, uv1) = IconUv(size, iconIsStatus ? StatusIconAspect : 1f, crop, marginLeft, marginRight);

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

        // Glow around the icon, as the trigger that decides asks for.
        var rule = state.Rule;
        if (rule is not null
            && (rule.Glow
                || (rule.GlowWhenReady && state.Phase == AuraPhase.Ready)
                || (rule.GlowWhenHighlighted && state.Highlighted)))
        {
            DrawGlow(drawList, pos, size, rounding);
        }

        // The texts over the icon. Every text has its own source, size (height of the letters, in logical pixels: the fonts are
        // already built scaled by the user's UI scale, so it is not multiplied again), position and colour.
        foreach (var text in definition.Texts)
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

            DrawOverlayText(drawList, value, Math.Clamp(text.Size, 4, 64), text.Font, pos, size,
                text.Anchor, new Vector2(text.OffsetX, text.OffsetY), text.Color);
        }
    }

    // What one source of a text gives right now, or null when it has nothing to show (no time left, no stacks...).
    private string? SourceValue(AuraTextSource text, AuraState state)
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
    /// The game icon of an aura: the one chosen by the user, or the one of what it tracks. "isStatusIcon" tells the shape
    /// (status icons are taller than ability icons). <paramref name="currentForm"/> is the form the game shows for an ability now (0 = the chosen one).
    /// </summary>
    internal static uint ResolveIcon(AuraRule? rule, uint currentForm, out bool isStatusIcon)
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

    // The "proc" glow of the game's hotbar: short golden dashes with gaps that run around the edge of the icon.
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

    // The part of the icon texture to show so that it fills a box of the given shape without distortion.
    internal static (Vector2 Uv0, Vector2 Uv1) IconUv(Vector2 boxSize, float iconAspect, float inset, float marginLeft = 0f, float marginRight = 0f)
    {
        // The empty margin of the icon (when asked) is cut at the sides, on top of the crop.
        var u0 = Math.Max(inset, marginLeft);
        var u1 = 1f - Math.Max(inset, marginRight);
        var v0 = inset;
        var v1 = 1f - inset;

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

    /// <summary>Below this many seconds the timers show tenths. Copied from the settings every frame. 0 = never.</summary>
    internal static int DecimalsBelowSeconds { get; private set; } = 3;

    // The timer as a single plain text: "17m", "28" or "2.5" (tenths only in the last seconds). No spacing tricks.
    internal static string FormatTime(float seconds)
    {
        if (seconds >= 60f)
            return $"{(int)(seconds / 60f)}m";

        if (seconds < DecimalsBelowSeconds)
            return seconds.ToString("0.0");

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
