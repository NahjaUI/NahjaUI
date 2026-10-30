using System;
using System.Collections.Generic;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Auras;

/// <summary>What an aura watches.</summary>
public enum AuraTriggerType
{
    /// <summary>An enhancement or enfeeblement on a unit.</summary>
    Status,

    /// <summary>The cooldown of one of your own abilities (with its charges and, optionally, its effect).</summary>
    Cooldown,

    /// <summary>An ability that the game turns on right now (it glows, or its button changes into it): procs and follow-up steps.</summary>
    Activated
}

/// <summary>Where the value of a text on an aura comes from.</summary>
public enum TextSource
{
    /// <summary>The time of the aura itself: the status time, or the cooldown (or the effect time) of the ability.</summary>
    OwnTime,

    /// <summary>The stacks of the status of the aura, or the charges of its ability.</summary>
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

/// <summary>Where the picture of an aura comes from.</summary>
public enum IconSource
{
    /// <summary>The icon of what the aura tracks: the status, or the ability (in its current form).</summary>
    Automatic,

    /// <summary>The icon of a status chosen by the user, whatever the aura tracks.</summary>
    Status,

    /// <summary>The icon of an ability chosen by the user, whatever the aura tracks.</summary>
    Ability
}

/// <summary>What has to be true for an icon trigger to match.</summary>
public enum ConditionKind
{
    /// <summary>An enhancement or enfeeblement is on the unit.</summary>
    StatusPresent,

    /// <summary>An enhancement or enfeeblement is NOT on the unit (a reminder that it fell off).</summary>
    StatusMissing,

    /// <summary>The game turns an ability on right now: it glows, or its button changes into it (procs, follow-up steps).</summary>
    AbilityActivated,

    /// <summary>An ability is recharging.</summary>
    AbilityOnCooldown,

    /// <summary>An ability is ready to use.</summary>
    AbilityReady,

    /// <summary>An ability is known: matches ready or recharging.</summary>
    AbilityAlways
}

/// <summary>
/// One icon trigger of an aura. The triggers of an aura are tried in order: the first one that matches decides the picture,
/// the glow and the values of the texts. When none matches, the aura is hidden.
/// </summary>
[Serializable]
public class AuraRule
{
    public ConditionKind Kind { get; set; } = ConditionKind.StatusPresent;

    /// <summary>Status conditions: the unit to look at.</summary>
    public AuraUnit Unit { get; set; } = AuraUnit.Player;

    public uint StatusId { get; set; }
    public uint ActionId { get; set; }

    /// <summary>Status conditions: only count a status the player applied.</summary>
    public bool OnlyMine { get; set; }

    /// <summary>Ability conditions: treat the global cooldown (about 2.5 s) as "ready".</summary>
    public bool IgnoreGlobalCooldown { get; set; } = true;

    /// <summary>The picture shown while this trigger decides. It can differ from what is tracked.</summary>
    public IconSource Icon { get; set; } = IconSource.Automatic;
    public uint IconStatusId { get; set; }
    public uint IconActionId { get; set; }

    /// <summary>Glow around the icon while this trigger decides.</summary>
    public bool Glow { get; set; }

    /// <summary>Glow while the ability is ready (for the "always" condition, which also covers recharging).</summary>
    public bool GlowWhenReady { get; set; }

    /// <summary>Glow while the game makes the button of the ability glow.</summary>
    public bool GlowWhenHighlighted { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    public bool IsAbility => Kind is not (ConditionKind.StatusPresent or ConditionKind.StatusMissing);
}

/// <summary>One place a text can take its value from. A text tries its sources in order: the first one with a value wins.</summary>
[Serializable]
public class AuraTextSource
{
    public TextSource Source { get; set; } = TextSource.OwnTime;
    public uint StatusId { get; set; }
    public uint ActionId { get; set; }
}

/// <summary>One text drawn over the icon of an aura, with its own source and look.</summary>
[Serializable]
public class AuraTextDefinition
{
    /// <summary>The places the value can come from, in order of priority.</summary>
    public List<AuraTextSource> Sources { get; set; } = new();

    // Older saves had one source. It becomes the first entry of Sources when the aura loads.
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

/// <summary>For cooldown auras: in which situations the icon is shown.</summary>
public enum CooldownShowMode
{
    /// <summary>Always: ready, recharging and while the effect is active.</summary>
    Always,

    /// <summary>Only while the ability is recharging or its effect is active.</summary>
    OnlyOnCooldown,

    /// <summary>Only while the ability is ready to use.</summary>
    OnlyWhenReady
}

/// <summary>For cooldown auras: how the icon looks while there is no charge to use.</summary>
public enum CooldownLook
{
    Normal,
    Dimmed,
    Red
}

/// <summary>Which unit an aura watches.</summary>
public enum AuraUnit
{
    Player,
    Target,
    Focus,
    TargetOfTarget
}

/// <summary>When the icon is shown.</summary>
public enum AuraShowMode
{
    /// <summary>While the status is on the unit (a normal enhancement or enfeeblement tracker).</summary>
    WhenPresent,

    /// <summary>While the status is NOT on the unit (a reminder that something fell off).</summary>
    WhenMissing
}

public enum AuraCombatMode
{
    Any,
    InCombat,
    OutOfCombat
}

/// <summary>Rules that decide whether an aura is active at all. An aura that is not loaded costs nothing.</summary>
[Serializable]
public class AuraLoadConditions
{
    /// <summary>Jobs the aura loads on. Empty means every job.</summary>
    public List<uint> JobIds { get; set; } = new();

    public AuraCombatMode Combat { get; set; } = AuraCombatMode.Any;
}

/// <summary>
/// One aura, as saved in the configuration. Auras are DATA, not code:
/// they can be edited, exported and shared safely.
/// Position and size are not stored here, they belong to the layout system.
/// </summary>
[Serializable]
public class AuraDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New aura";
    public bool Enabled { get; set; } = true;

    /// <summary>Id of the group that places this aura, or null when the aura has its own position.</summary>
    public string? GroupId { get; set; }

    /// <summary>The icon triggers, in order of priority: the first one that matches decides the picture and the glow.</summary>
    public List<AuraRule> Rules { get; set; } = new();

    /// <summary>False for auras saved before the trigger list existed: their old settings become one trigger when they load.</summary>
    public bool RulesMigrated { get; set; }

    // ---- Older saves (before the trigger list). Only read once, to build Rules; nothing uses them afterwards.
    public AuraTriggerType Trigger { get; set; } = AuraTriggerType.Status;
    public IconSource Icon { get; set; } = IconSource.Automatic;
    public uint IconStatusId { get; set; }
    public uint IconActionId { get; set; }

    /// <summary>The texts drawn over the icon: any number, each with its own source (Text 1, Text 2...).</summary>
    public List<AuraTextDefinition> Texts { get; set; } = new();

    /// <summary>False for auras saved before the text list existed: their timer and stack texts become Texts when they load.</summary>
    public bool TextsMigrated { get; set; }

    // Status triggers: the status to watch. Cooldown triggers: the optional effect of the ability,
    // whose remaining time is shown while it is active.
    public AuraUnit Unit { get; set; } = AuraUnit.Player;
    public uint StatusId { get; set; }

    /// <summary>
    /// Cooldown auras: more effects that count as "the effect is active" (for abilities that give two enhancements one after
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

    /// <summary>Glow while the game itself makes the button of the ability glow (follow-ups like Goring Blade, procs).</summary>
    public bool GlowWhenHighlighted { get; set; }

    /// <summary>
    /// Cooldown auras: show the icon only while this ability is the next step of a combo (the game's own combo: you just used
    /// the ability it follows, and the combo timer is still running). The timer shows the seconds left to continue the combo.
    /// </summary>
    public bool OnlyAsComboStep { get; set; }

    /// <summary>Treat the global cooldown (about 2.5 s) as "ready".</summary>
    public bool IgnoreGlobalCooldown { get; set; } = true;

    /// <summary>Only count the status if the player applied it.</summary>
    public bool OnlyMine { get; set; }

    public AuraShowMode ShowMode { get; set; } = AuraShowMode.WhenPresent;

    // Display
    public bool ShowTimer { get; set; } = true;
    public bool ShowStacks { get; set; } = true;

    /// <summary>Font size of the timer, in pixels. One of the sizes in OverlayFonts.AvailableSizes.</summary>
    public int TextSize { get; set; } = 8;

    /// <summary>Size of the charges / stack count, in pixels (the height of its letters). Chosen like the timer's; 0 in old saved auras counts as 8.</summary>
    public int StackTextSize { get; set; } = 8;

    /// <summary>Font family of the timer and stack text.</summary>
    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    // Where the texts sit on the icon. The anchor is a point of the icon (corner, edge or centre): the same
    // point of the text is attached to it, so a right anchor right-justifies the text and a centre anchor centres it.
    // The offset moves it from there, in pixels (+X right, +Y down).
    public AnchorPoint TimerAnchor { get; set; } = AnchorPoint.Center;
    public int TimerOffsetX { get; set; }
    public int TimerOffsetY { get; set; }

    // The number of charges (cooldown auras) or stacks (status auras).
    public AnchorPoint StacksAnchor { get; set; } = AnchorPoint.BottomRight;
    public int StacksOffsetX { get; set; } = -2;
    public int StacksOffsetY { get; set; } = -1;

    /// <summary>
    /// How much of the icon's outer edge is cut off, in percent. The game's icons have rounded corners drawn
    /// in the image itself; cutting a little more hides them.
    /// </summary>
    public int IconCropPercent { get; set; } = 13;

    /// <summary>Cut the empty margin at the left and right of the game's icon, so the art fills the width of the box.</summary>
    public bool FillWidth { get; set; } = true;

    /// <summary>Corner rounding of the icon, in pixels. The same idea as the unit frames, so the interface looks consistent. 0 = square.</summary>
    public int CornerRadiusPx { get; set; } = 3;

    // Load conditions
    public AuraLoadConditions Load { get; set; } = new();

    /// <summary>
    /// Copies the display settings and the load conditions of another aura.
    /// The name, the trigger, the group and the position are not touched.
    /// </summary>
    public void CopyStyleFrom(AuraDefinition other)
    {
        IconCropPercent = other.IconCropPercent;
        FillWidth = other.FillWidth;
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

        Load = new AuraLoadConditions
        {
            Combat = other.Load.Combat,
            JobIds = new List<uint>(other.Load.JobIds)
        };
    }
}
