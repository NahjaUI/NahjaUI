using System;
using System.Collections.Generic;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Auras;

/// <summary>What an aura watches.</summary>
public enum AuraTriggerType
{
    /// <summary>A buff or debuff on a unit.</summary>
    Status,

    /// <summary>The cooldown of one of your own abilities (with its charges and, optionally, its effect).</summary>
    Cooldown
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
    /// <summary>While the status is on the unit (a normal buff or debuff tracker).</summary>
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

    // Trigger: what is watched.
    public AuraTriggerType Trigger { get; set; } = AuraTriggerType.Status;

    // Status triggers: the status to watch. Cooldown triggers: the optional effect of the ability,
    // whose remaining time is shown while it is active.
    public AuraUnit Unit { get; set; } = AuraUnit.Player;
    public uint StatusId { get; set; }

    /// <summary>
    /// Cooldown auras: more effects that count as "the effect is active" (for abilities that give two buffs one after
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
    public int TextSize { get; set; } = 12;

    /// <summary>
    /// Font size of the charges / stack count, in pixels. 0 means automatic: three quarters of the timer size
    /// (how it always was), so existing auras look the same.
    /// </summary>
    public int StackTextSize { get; set; } = 0;

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
