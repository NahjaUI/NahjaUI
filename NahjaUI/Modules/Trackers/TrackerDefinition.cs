using System;
using System.Collections.Generic;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Trackers;

/// <summary>What a tracker watches.</summary>
public enum TrackerTriggerType
{
    /// <summary>An enhancement or enfeeblement on a unit.</summary>
    Status,

    /// <summary>The cooldown of one of your own abilities (with its charges and, optionally, its effect).</summary>
    Cooldown,

    /// <summary>An ability that the game turns on right now (it glows, or its button changes into it): glowing actions and follow-up steps.</summary>
    Activated
}

/// <summary>Where the value of a text on a tracker comes from.</summary>
public enum TextSource
{
    /// <summary>The time of the tracker itself: the status time, or the cooldown (or the effect time) of the ability.</summary>
    OwnTime,

    /// <summary>The stacks of the status of the tracker, or the charges of its ability.</summary>
    OwnStacks,

    /// <summary>The time left of the game's combo.</summary>
    ComboTime,

    /// <summary>The time left of a status chosen by the user (on the player).</summary>
    StatusTime,

    /// <summary>The stacks of a status chosen by the user (on the player).</summary>
    StatusStacks,

    /// <summary>The cooldown of an ability chosen by the user.</summary>
    AbilityCooldown,

    /// <summary>The charges of an ability chosen by the user.</summary>
    AbilityCharges
}

/// <summary>Where the picture of a tracker comes from.</summary>
public enum IconSource
{
    /// <summary>The icon of what the tracker tracks: the status, or the ability (in its current form).</summary>
    Automatic,

    /// <summary>The icon of a status chosen by the user, whatever the tracker tracks.</summary>
    Status,

    /// <summary>The icon of an ability chosen by the user, whatever the tracker tracks.</summary>
    Ability
}

/// <summary>What has to be true for an icon trigger to match.</summary>
public enum ConditionKind
{
    /// <summary>An enhancement or enfeeblement is on the unit.</summary>
    StatusPresent,

    /// <summary>An enhancement or enfeeblement is NOT on the unit (a reminder that it fell off).</summary>
    StatusMissing,

    /// <summary>The game turns an ability on right now: it glows, or its button changes into it (glowing actions, follow-up steps).</summary>
    AbilityActivated,

    /// <summary>An ability is recharging.</summary>
    AbilityOnCooldown,

    /// <summary>An ability is ready to use.</summary>
    AbilityReady,

    /// <summary>An ability is known: matches ready or recharging.</summary>
    AbilityAlways,

    /// <summary>The icon is always there, whether the status is on the unit or not (a DoT to keep up, an enhancement to refresh).</summary>
    StatusAlways
}

/// <summary>When an effect of an icon trigger (glow, red mask, dimmed) is applied.</summary>
public enum EffectWhen
{
    Never,

    /// <summary>Whenever this trigger decides.</summary>
    Always,

    /// <summary>"Status always" triggers: while the status is on the unit.</summary>
    StatusOn,

    /// <summary>"Status always" triggers: while the status is NOT on the unit.</summary>
    StatusMissing,

    /// <summary>Ability triggers that cover ready and recharging: while ready.</summary>
    Ready,

    /// <summary>Ability triggers that cover ready and recharging: while recharging.</summary>
    Recharging,

    /// <summary>Ability triggers: while the game makes the button of the ability glow.</summary>
    Highlighted,

    /// <summary>Ability triggers: while it can be used now (ready, or recharging but with a charge left).</summary>
    Usable,

    /// <summary>Ability triggers: while it cannot be used (recharging with no charge left).</summary>
    Unavailable
}

/// <summary>
/// One icon trigger of a tracker. The triggers of a tracker are tried in order: the first one that matches decides the picture,
/// the glow and the values of the texts. When none matches, the tracker is hidden.
/// </summary>
[Serializable]
public class TrackerRule
{
    public ConditionKind Kind { get; set; } = ConditionKind.StatusPresent;

    /// <summary>Status conditions: the unit to look at.</summary>
    public TrackerUnit Unit { get; set; } = TrackerUnit.Player;

    public uint StatusId { get; set; }
    public uint ActionId { get; set; }

    /// <summary>Status conditions: the id of a filter of the Filters page. When set, any status of the filter counts (instead of the one status above).</summary>
    public string FilterId { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonIgnore]
    public bool UsesFilter => !string.IsNullOrEmpty(FilterId);

    /// <summary>Status conditions: only count a status the player applied.</summary>
    public bool OnlyMine { get; set; }

    /// <summary>Ability conditions: treat the global cooldown (about 2.5 s) as "ready".</summary>
    public bool IgnoreGlobalCooldown { get; set; } = true;

    /// <summary>The texts drawn over the icon while this trigger decides: any number, each with its own value, size, font, position and colour.</summary>
    public List<TrackerTextDefinition> Texts { get; set; } = new();

    /// <summary>The picture shown while this trigger decides. It can differ from what is tracked.</summary>
    public IconSource Icon { get; set; } = IconSource.Automatic;
    public uint IconStatusId { get; set; }
    public uint IconActionId { get; set; }

    // The three effects of a trigger: each one has its own "when".
    public EffectWhen GlowWhen { get; set; } = EffectWhen.Never;

    /// <summary>A red mask over the icon (the border stays black).</summary>
    public EffectWhen RedWhen { get; set; } = EffectWhen.Never;

    /// <summary>The icon darkened.</summary>
    public EffectWhen DimWhen { get; set; } = EffectWhen.Never;

    /// <summary>False for triggers saved before the effects had a "when": the old glow flags and the tracker's recharge look become effects once.</summary>
    public bool EffectsMigrated { get; set; }

    // ---- Older saves (only read once, to build the effects above).
    public bool Glow { get; set; }

    /// <summary>Glow while the ability is ready (for the "always" condition, which also covers recharging).</summary>
    public bool GlowWhenReady { get; set; }

    /// <summary>"Status always": glow while the status is NOT on the unit (time to refresh it).</summary>
    public bool GlowWhenMissing { get; set; }

    /// <summary>Glow while the game makes the button of the ability glow.</summary>
    public bool GlowWhenHighlighted { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    public bool IsAbility => Kind is not (ConditionKind.StatusPresent or ConditionKind.StatusMissing or ConditionKind.StatusAlways);
}

/// <summary>One place a text can take its value from. A text tries its sources in order: the first one with a value wins.</summary>
[Serializable]
public class TrackerTextSource
{
    public TextSource Source { get; set; } = TextSource.OwnTime;
    public uint StatusId { get; set; }
    public uint ActionId { get; set; }
}

/// <summary>One text drawn over the icon of a tracker, with its own source and look.</summary>
[Serializable]
public class TrackerTextDefinition
{
    /// <summary>The places the value can come from, in order of priority.</summary>
    public List<TrackerTextSource> Sources { get; set; } = new();

    // Older saves had one source. It becomes the first entry of Sources when the tracker loads.
    public TextSource Source { get; set; } = TextSource.OwnTime;
    public uint StatusId { get; set; }
    public uint ActionId { get; set; }

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    /// <summary>Height of the letters, in logical pixels.</summary>
    public int Size { get; set; } = 8;

    public AnchorPoint Anchor { get; set; } = AnchorPoint.Center;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public System.Numerics.Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
}

/// <summary>For cooldown trackers: in which situations the icon is shown.</summary>
public enum CooldownShowMode
{
    /// <summary>Always: ready, recharging and while the effect is active.</summary>
    Always,

    /// <summary>Only while the ability is recharging or its effect is active.</summary>
    OnlyOnCooldown,

    /// <summary>Only while the ability is ready to use.</summary>
    OnlyWhenReady
}

/// <summary>For cooldown trackers: how the icon looks while there is no charge to use.</summary>
public enum CooldownLook
{
    Normal,
    Dimmed,
    Red
}

/// <summary>Which unit a tracker watches.</summary>
public enum TrackerUnit
{
    Player,
    Target,
    Focus,
    TargetOfTarget
}

/// <summary>Which empty margins of the game's icon are cut away, so the art fills the box.</summary>
public enum IconTrim
{
    None,
    Sides,
    TopAndBottom,
    AllAround
}

/// <summary>When the icon is shown.</summary>
public enum TrackerShowMode
{
    /// <summary>While the status is on the unit (a normal enhancement or enfeeblement tracker).</summary>
    WhenPresent,

    /// <summary>While the status is NOT on the unit (a reminder that something fell off).</summary>
    WhenMissing
}

public enum TrackerCombatMode
{
    Any,
    InCombat,
    OutOfCombat
}

/// <summary>In which kind of content a tracker loads. The statuses and abilities of PvP are not the ones of PvE, so trackers are for PvE unless said otherwise.</summary>
public enum TrackerPvpMode
{
    PveOnly,
    PvpOnly,
    Everywhere
}

/// <summary>Rules that decide whether a tracker is active at all. A tracker that is not loaded costs nothing.</summary>
[Serializable]
public class TrackerLoadConditions
{
    /// <summary>Jobs the tracker loads on. Empty means every job.</summary>
    public List<uint> JobIds { get; set; } = new();

    public TrackerCombatMode Combat { get; set; } = TrackerCombatMode.Any;

    /// <summary>PvE only by default (also for trackers saved before this option existed).</summary>
    public TrackerPvpMode Where { get; set; } = TrackerPvpMode.PveOnly;
}

/// <summary>
/// One tracker, as saved in the configuration. Trackers are DATA, not code:
/// they can be edited, exported and shared safely.
/// Position and size are not stored here, they belong to the layout system.
/// </summary>
[Serializable]
public class TrackerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New tracker";
    public bool Enabled { get; set; } = true;

    /// <summary>Id of the group that places this tracker, or null when the tracker has its own position.</summary>
    public string? GroupId { get; set; }

    /// <summary>The icon triggers, in order of priority: the first one that matches decides the picture and the glow.</summary>
    public List<TrackerRule> Rules { get; set; } = new();

    /// <summary>False for trackers saved before the trigger list existed: their old settings become one trigger when they load.</summary>
    public bool RulesMigrated { get; set; }

    // ---- Older saves (before the trigger list). Only read once, to build Rules; nothing uses them afterwards.
    public TrackerTriggerType Trigger { get; set; } = TrackerTriggerType.Status;
    public IconSource Icon { get; set; } = IconSource.Automatic;
    public uint IconStatusId { get; set; }
    public uint IconActionId { get; set; }

    /// <summary>Older saves only: the texts used to belong to the tracker. They are copied into every trigger when the tracker loads (RuleTextsMigrated) and this list is emptied.</summary>
    public List<TrackerTextDefinition> Texts { get; set; } = new();

    /// <summary>False for trackers saved before the text list existed: their timer and stack texts become Texts when they load.</summary>
    public bool TextsMigrated { get; set; }

    /// <summary>False for trackers saved before every trigger had its own texts: the tracker's texts are copied into each trigger once.</summary>
    public bool RuleTextsMigrated { get; set; }

    // Status triggers: the status to watch. Cooldown triggers: the optional effect of the ability,
    // whose remaining time is shown while it is active.
    public TrackerUnit Unit { get; set; } = TrackerUnit.Player;
    public uint StatusId { get; set; }

    /// <summary>
    /// Cooldown trackers: more effects that count as "the effect is active" (for abilities that give two enhancements one after
    /// the other, like Fight or Flight and Goring Blade Ready). The first one found, in this order after StatusId, is shown.
    /// </summary>
    public List<uint> ExtraEffectIds { get; set; } = new();

    // Cooldown triggers
    public uint ActionId { get; set; }
    public CooldownShowMode CooldownShow { get; set; } = CooldownShowMode.Always;
    public CooldownLook CooldownLook { get; set; } = CooldownLook.Red;
    public bool ShowCooldownSweep { get; set; } = true;
    public bool GlowWhenReady { get; set; }
    public bool GlowWhenActive { get; set; }

    /// <summary>Glow while the game itself makes the button of the ability glow (follow-ups like Goring Blade, ready actions).</summary>
    public bool GlowWhenHighlighted { get; set; }

    /// <summary>
    /// Cooldown trackers: show the icon only while this ability is the next step of a combo (the game's own combo: you just used
    /// the ability it follows, and the combo timer is still running). The timer shows the seconds left to continue the combo.
    /// </summary>
    public bool OnlyAsComboStep { get; set; }

    /// <summary>Treat the global cooldown (about 2.5 s) as "ready".</summary>
    public bool IgnoreGlobalCooldown { get; set; } = true;

    /// <summary>Only count the status if the player applied it.</summary>
    public bool OnlyMine { get; set; }

    public TrackerShowMode ShowMode { get; set; } = TrackerShowMode.WhenPresent;

    // Display
    public bool ShowTimer { get; set; } = true;
    public bool ShowStacks { get; set; } = true;

    /// <summary>Font size of the timer, in pixels. One of the sizes in OverlayFonts.AvailableSizes.</summary>
    public int TextSize { get; set; } = 8;

    /// <summary>Size of the charges / stack count, in pixels (the height of its letters). Chosen like the timer's; 0 in old saved trackers counts as 8.</summary>
    public int StackTextSize { get; set; } = 9;

    /// <summary>Font family of the timer and stack text.</summary>
    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    // Where the texts sit on the icon. The anchor is a point of the icon (corner, edge or centre): the same
    // point of the text is attached to it, so a right anchor right-justifies the text and a centre anchor centres it.
    // The offset moves it from there, in pixels (+X right, +Y down).
    public AnchorPoint TimerAnchor { get; set; } = AnchorPoint.Center;
    public int TimerOffsetX { get; set; }
    public int TimerOffsetY { get; set; }

    // The number of charges (cooldown trackers) or stacks (status trackers).
    public AnchorPoint StacksAnchor { get; set; } = AnchorPoint.BottomRight;
    public int StacksOffsetX { get; set; }
    public int StacksOffsetY { get; set; }

    /// <summary>
    /// How much of the icon's outer edge is cut off, in percent. The game's icons have rounded corners drawn
    /// in the image itself; cutting a little more hides them.
    /// </summary>
    public int IconCropPercent { get; set; } = 13;

    /// <summary>Which margins of the game's icon are cut away. Null in trackers saved before this existed: it is built from FillWidth when they load.</summary>
    public IconTrim? Trim { get; set; }

    // Older saves: "Fill the width" (now Trim = Sides) and the crop percentage (gone).
    public bool FillWidth { get; set; } = true;

    /// <summary>Corner rounding of the icon, in pixels. The same idea as the unit frames, so the interface looks consistent. 0 = square.</summary>
    public int CornerRadiusPx { get; set; } = 6;

    // Load conditions
    public TrackerLoadConditions Load { get; set; } = new();

    /// <summary>
    /// Copies the display settings and the load conditions of another tracker.
    /// The name, the trigger, the group and the position are not touched.
    /// </summary>
    public void CopyStyleFrom(TrackerDefinition other)
    {
        IconCropPercent = other.IconCropPercent;
        Trim = other.Trim ?? Trim;
        CornerRadiusPx = other.CornerRadiusPx;
        ShowTimer = other.ShowTimer;
        ShowStacks = other.ShowStacks;
        TextSize = other.TextSize;
        StackTextSize = other.StackTextSize;
        Font = other.Font;
        TimerAnchor = other.TimerAnchor;
        TimerOffsetX = other.TimerOffsetX;
        TimerOffsetY = other.TimerOffsetY;
        StacksAnchor = other.StacksAnchor;
        StacksOffsetX = other.StacksOffsetX;
        StacksOffsetY = other.StacksOffsetY;
        CooldownLook = other.CooldownLook;
        ShowCooldownSweep = other.ShowCooldownSweep;
        GlowWhenReady = other.GlowWhenReady;
        GlowWhenActive = other.GlowWhenActive;
        GlowWhenHighlighted = other.GlowWhenHighlighted;
        ExtraEffectIds = new List<uint>(other.ExtraEffectIds);
        IgnoreGlobalCooldown = other.IgnoreGlobalCooldown;

        Load = new TrackerLoadConditions
        {
            Combat = other.Load.Combat,
            Where = other.Load.Where,
            JobIds = new List<uint>(other.Load.JobIds)
        };
    }
}
