using System;
using System.Collections.Generic;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.PartyFrames;

/// <summary>Which party members get a mana bar.</summary>
public enum PartyManaMode
{
    Off,

    /// <summary>White Mage, Scholar, Astrologian and Sage.</summary>
    Healers,

    /// <summary>The healers, plus the jobs that can raise: Summoner and Red Mage.</summary>
    HealersAndRaisers,

    Everyone
}

/// <summary>The order of the bars.</summary>
/// <summary>Which of the game's own job icons is used (the game draws them; nothing is copied into the plugin).</summary>
public enum PartyJobIconStyle
{
    /// <summary>Icon 62000 + job (plain).</summary>
    Plain,

    /// <summary>Icon 62100 + job (the lighter set).</summary>
    Light
}

public enum PartyOrder
{
    /// <summary>The same order as the game's own party list, and it follows the game's option (you first, by role...) by itself.</summary>
    GameOrder,

    /// <summary>You first, then the others in the order of the game's list.</summary>
    MeFirst
}

/// <summary>Which statuses a row of party icons shows.</summary>
public enum PartyAuraFilter
{
    /// <summary>Every buff (or every debuff, in the debuff row).</summary>
    All,

    /// <summary>Only the statuses in the list.</summary>
    OnlyListed,

    /// <summary>Everything except the statuses in the list.</summary>
    AllExceptListed
}

/// <summary>The direction in which the icons of a row are added.</summary>
public enum PartyAuraGrowth
{
    Left,
    Right,
    Up,
    Down
}

/// <summary>
/// One row of status icons next to each party bar (the buffs, or the debuffs). It has its own point on the bar, its own
/// offset, icon size, spacing and growth, so the two rows can be put anywhere.
/// </summary>
[Serializable]
public class PartyAuraSettings
{
    public bool Enabled { get; set; } = true;

    public PartyAuraFilter Filter { get; set; } = PartyAuraFilter.All;

    /// <summary>The statuses of the filter (status ids).</summary>
    public List<uint> StatusIds { get; set; } = new();

    /// <summary>
    /// Statuses that are always shown, whoever applied them (a raid debuff you want to see even if it is not yours).
    /// They count even when "only the ones I applied" is on.
    /// </summary>
    public List<uint> ExtraStatusIds { get; set; } = new();

    /// <summary>Only the statuses that you applied.</summary>
    public bool OnlyMine { get; set; }

    /// <summary>The point of the party bar the row is attached to.</summary>
    public AnchorPoint BarAnchor { get; set; } = AnchorPoint.Left;

    /// <summary>The point of the first icon that sits on that point of the bar.</summary>
    public AnchorPoint IconAnchor { get; set; } = AnchorPoint.Right;

    public int OffsetX { get; set; } = -8;

    public int OffsetY { get; set; }

    /// <summary>Icon size in pixels. Rectangular like the auras: wider than tall.</summary>
    public int IconWidth { get; set; } = 32;

    public int IconHeight { get; set; } = 24;

    /// <summary>The icon is cropped, never stretched. This is how much of its edge is cut, in percent (the game's icons have rounded corners).</summary>
    public int CropPercent { get; set; } = 13;

    /// <summary>Corner rounding of the icons, in pixels, like the bars. 0 = square.</summary>
    public int CornerRadiusPx { get; set; } = 3;

    public int SpacingPx { get; set; } = 3;

    public int MaxIcons { get; set; } = 5;

    public PartyAuraGrowth Growth { get; set; } = PartyAuraGrowth.Left;

    public bool ShowTimer { get; set; } = true;

    /// <summary>Font size of the time left, in pixels (one of the sizes in OverlayFonts.AvailableSizes).</summary>
    public int TimerSize { get; set; } = 12;

    /// <summary>Font of the time left and of the stack count.</summary>
    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    // Where the texts sit on the icon. The anchor is a point of the icon: the same point of the text is attached to it, so a
    // right anchor right-justifies the text and a centre anchor centres it. The offset moves it from there, in pixels.
    public AnchorPoint TimerAnchor { get; set; } = AnchorPoint.Center;

    public int TimerOffsetX { get; set; }

    public int TimerOffsetY { get; set; }

    public bool ShowStacks { get; set; } = true;

    /// <summary>Font size of the stack count, in pixels. 0 means automatic: three quarters of the time size.</summary>
    public int StackTextSize { get; set; } = 0;

    public AnchorPoint StacksAnchor { get; set; } = AnchorPoint.BottomRight;

    public int StacksOffsetX { get; set; } = -2;

    public int StacksOffsetY { get; set; } = -1;

    /// <summary>The buffs row of the player, target and focus frames: above the bar, growing to the right. Off until it is switched on.</summary>
    public static PartyAuraSettings CreateFrameBuffs() => new()
    {
        Enabled = false,
        BarAnchor = AnchorPoint.TopLeft,
        IconAnchor = AnchorPoint.BottomLeft,
        OffsetX = 0,
        OffsetY = -14,
        Growth = PartyAuraGrowth.Right,
        MaxIcons = 12
    };

    /// <summary>The debuffs row of the player, target and focus frames: under the bar, growing to the right, only the ones you applied.</summary>
    public static PartyAuraSettings CreateFrameDebuffs() => new()
    {
        Enabled = false,
        BarAnchor = AnchorPoint.BottomLeft,
        IconAnchor = AnchorPoint.TopLeft,
        OffsetX = 0,
        OffsetY = 6,
        Growth = PartyAuraGrowth.Right,
        OnlyMine = true,
        MaxIcons = 12
    };

    /// <summary>Buffs go at the left of the bar and grow to the left.</summary>
    public static PartyAuraSettings CreateBuffs() => new();

    /// <summary>Debuffs go at the right of the bar and grow to the right.</summary>
    public static PartyAuraSettings CreateDebuffs() => new()
    {
        BarAnchor = AnchorPoint.Right,
        IconAnchor = AnchorPoint.Left,
        OffsetX = 8,
        Growth = PartyAuraGrowth.Right
    };
}

/// <summary>
/// The party frames: one bar per member, one under the other. The look of each bar (colours, shield, name and life text,
/// size of the first bar's box) is a normal <see cref="UnitFrameSettings"/>, so it is edited with the same controls as
/// the other unit frames. What is only about the party is here.
/// </summary>
[Serializable]
public class PartyFrameSettings
{
    /// <summary>The look of one member's bar. Its size and position belong to the layout element of the party frames.</summary>
    public UnitFrameSettings Look { get; set; } = CreateLook();

    /// <summary>Space between two bars, in pixels.</summary>
    public int RowSpacingPx { get; set; } = 6;

    public PartyManaMode ManaMode { get; set; } = PartyManaMode.HealersAndRaisers;

    public PartyOrder Order { get; set; } = PartyOrder.GameOrder;

    /// <summary>Your chocobo companion as one more bar at the end, like the game's own party list.</summary>
    public bool ShowChocobo { get; set; } = true;

    /// <summary>A ring around the bar of the member you have targeted.</summary>
    public bool SelectionRing { get; set; } = true;

    /// <summary>An arrow beside the bar of the member you have targeted.</summary>
    public bool SelectionArrow { get; set; } = true;

    public Vector4 SelectionColor { get; set; } = new(0.545f, 0.486f, 1f, 1f);

    /// <summary>A thin stripe at the left edge of each bar: blue for tanks, green for healers, red for damage dealers.</summary>
    public bool RoleStripe { get; set; } = true;

    /// <summary>The job icon of each member, inside the bar at its left. It works for every job, crafters and gatherers too.</summary>
    public bool ShowJobIcon { get; set; }

    public PartyJobIconStyle JobIconStyle { get; set; } = PartyJobIconStyle.Plain;

    /// <summary>Size of the icon in pixels; it never goes over the height of the bar.</summary>
    public int JobIconSizePx { get; set; } = 16;

    /// <summary>The buffs of each member, at the left of the bar by default.</summary>
    public PartyAuraSettings Buffs { get; set; } = PartyAuraSettings.CreateBuffs();

    /// <summary>The debuffs of each member, at the right of the bar by default.</summary>
    public PartyAuraSettings Debuffs { get; set; } = PartyAuraSettings.CreateDebuffs();

    /// <summary>The look the party bars start with: class colours, the name at the left and the percentage at the right.</summary>
    public static UnitFrameSettings CreateLook() => new()
    {
        HideNative = true,
        RoundingPx = 4,
        ShowMana = false,
        ManaHeightPx = 5,
        LifeColorMode = LifeColorMode.Class,
        HealthPreset = HealthTextPreset.Percent,
        Name = new FrameTextSettings { Anchor = AnchorPoint.Left, OffsetX = 10, OffsetY = 0, TextSize = 15, Font = OverlayFont.NahjaDisplayBold },
        Health = new FrameTextSettings { Anchor = AnchorPoint.Right, OffsetX = -8, OffsetY = 0, TextSize = 14, Font = OverlayFont.NahjaDisplayBold }
    };
}
