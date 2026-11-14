using System;
using System.Collections.Generic;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.Trackers;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.PartyListBar;

/// <summary>Which party members get a MP bar.</summary>
public enum PartyMpMode
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

/// <summary>Which statuses a row of party icons shows.</summary>
public enum StatusRowFilter
{
    /// <summary>Every enhancement (or every enfeeblement, in the enfeeblement row).</summary>
    All,

    /// <summary>Only the statuses in the list.</summary>
    OnlyListed,

    /// <summary>Everything except the statuses in the list.</summary>
    AllExceptListed
}

/// <summary>The order of the icons of a row, by the time they have left. A status without an end (permanent) counts as the longest possible time.</summary>
public enum StatusOrder
{
    /// <summary>The shortest time first, permanent statuses last.</summary>
    Ascending,

    /// <summary>The longest time first, permanent statuses first.</summary>
    Descending
}

/// <summary>The direction in which the icons of a row are added.</summary>
public enum StatusRowGrowth
{
    Left,
    Right,
    Up,
    Down,

    /// <summary>The row is always centered (horizontally) on its point, whatever the number of icons.</summary>
    CenteredHorizontally,

    /// <summary>The row is always centered (vertically) on its point, whatever the number of icons.</summary>
    CenteredVertically
}

/// <summary>What a row of icons is attached to.</summary>
public enum StatusRowAttach
{
    /// <summary>The bar of the frame.</summary>
    Frame,

    /// <summary>The other row of the same frame (the enfeeblements row for the enhancements one, and the other way round).</summary>
    OtherRow
}

/// <summary>
/// One row of status icons next to each party bar (the enhancements, or the enfeeblements). It has its own point on the bar, its own
/// offset, icon size, spacing and growth, so the two rows can be put anywhere.
/// </summary>
[Serializable]
public class StatusRowSettings
{
    public bool Enabled { get; set; } = true;

    public StatusRowFilter Filter { get; set; } = StatusRowFilter.All;

    /// <summary>The statuses of the filter (status ids).</summary>
    public List<uint> StatusIds { get; set; } = new();

    /// <summary>Filters of the Filters page whose statuses count as part of the list above.</summary>
    public List<string> FilterIds { get; set; } = new();

    /// <summary>
    /// When on, the statuses of the ticked filters come in the order of the filters (the first ticked filter first), whatever the time they have
    /// left; the time only orders the statuses inside each filter. When off, only the time orders the icons.
    /// </summary>
    public bool PriorityByFilter { get; set; }

    /// <summary>Filters (ids) whose statuses only count when you applied them. The other filters of the row keep showing everybody's.</summary>
    public List<string> OnlyMineFilterIds { get; set; } = new();

    /// <summary>
    /// Statuses that are always shown, whoever applied them (a raid enfeeblement you want to see even if it is not yours).
    /// They count even when "only the ones I applied" is on.
    /// </summary>
    public List<uint> ExtraStatusIds { get; set; } = new();

    /// <summary>Only the statuses that you applied.</summary>
    public bool OnlyMine { get; set; }

    /// <summary>What the row is attached to: the bar, or the other row of the same frame (if both choose each other, the enhancements row stays on the bar).</summary>
    public StatusRowAttach AttachTo { get; set; } = StatusRowAttach.Frame;

    /// <summary>The point of the party bar the row is attached to.</summary>
    public AnchorPoint BarAnchor { get; set; } = AnchorPoint.Left;

    /// <summary>The point of the first icon that sits on that point of the bar.</summary>
    public AnchorPoint IconAnchor { get; set; } = AnchorPoint.Right;

    public int OffsetX { get; set; } = -8;

    public int OffsetY { get; set; }

    /// <summary>Icon size in pixels. Rectangular like the trackers: wider than tall.</summary>
    public int IconWidth { get; set; } = 32;

    public int IconHeight { get; set; } = 24;

    /// <summary>The order of the icons: by the time left.</summary>
    public StatusOrder Order { get; set; } = StatusOrder.Ascending;

    public int SpacingPx { get; set; } = 3;

    public int MaxIcons { get; set; } = 5;

    /// <summary>Icons in one line: more icons than this go to the next line (the game holds up to 30 statuses on a unit).</summary>
    public int IconsPerLine { get; set; } = 6;

    public StatusRowGrowth Growth { get; set; } = StatusRowGrowth.Left;

    /// <summary>
    /// Where the next line goes when a line is full: Up or Down while the icons grow in a horizontal line (Left, Right, Centered horizontally),
    /// Left or Right while they grow in a vertical one. Any other value counts as the first choice (Down, Right).
    /// </summary>
    public StatusRowGrowth NextLine { get; set; } = StatusRowGrowth.Down;

    /// <summary>Does the line grow horizontally (the next line is above or under it) or vertically?</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool GrowsHorizontally => Growth is StatusRowGrowth.Left or StatusRowGrowth.Right or StatusRowGrowth.CenteredHorizontally;

    /// <summary>The side of the next line, always one that fits the growth.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public StatusRowGrowth NextLineNow => GrowsHorizontally
        ? (NextLine == StatusRowGrowth.Up ? StatusRowGrowth.Up : StatusRowGrowth.Down)
        : (NextLine == StatusRowGrowth.Left ? StatusRowGrowth.Left : StatusRowGrowth.Right);

    public bool ShowTimer { get; set; } = true;

    /// <summary>Font size of the time left, in pixels (one of the sizes in OverlayFonts.AvailableSizes).</summary>
    public int TimerSize { get; set; } = 8;

    // The colors of these two texts come from Appearance (the same for every icon row).
    [Newtonsoft.Json.JsonIgnore]
    public Vector4 TimerColor => NahjaUI.Core.Ui.GlobalColors.Current.TimerText;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 StacksColor => NahjaUI.Core.Ui.GlobalColors.Current.StacksText;

    // Where the texts sit on the icon. The anchor is a point of the icon: the same point of the text is attached to it, so a
    // right anchor right-justifies the text and a centre anchor centres it. The offset moves it from there, in pixels.
    public AnchorPoint TimerAnchor { get; set; } = AnchorPoint.Center;

    public int TimerOffsetX { get; set; }

    public int TimerOffsetY { get; set; }

    public bool ShowStacks { get; set; } = true;

    /// <summary>Size of the stack count, in pixels (the height of its letters).</summary>
    public int StackTextSize { get; set; } = 8;

    public AnchorPoint StacksAnchor { get; set; } = AnchorPoint.BottomRight;

    public int StacksOffsetX { get; set; } = -2;

    public int StacksOffsetY { get; set; } = -1;

    /// <summary>The enhancements row of the player, target and focus frames: above the bar, growing to the right. Off until it is switched on.</summary>
    public static StatusRowSettings CreateFrameEnhancements() => new()
    {
        Enabled = false,
        BarAnchor = AnchorPoint.TopLeft,
        IconAnchor = AnchorPoint.BottomLeft,
        OffsetX = 0,
        OffsetY = -14,
        Growth = StatusRowGrowth.Right,
        MaxIcons = 12
    };

    /// <summary>The enfeeblements row of the player, target and focus frames: under the bar, growing to the right, all of them (whoever applied them).</summary>
    public static StatusRowSettings CreateFrameEnfeeblements() => new()
    {
        Enabled = false,
        BarAnchor = AnchorPoint.BottomLeft,
        IconAnchor = AnchorPoint.TopLeft,
        OffsetX = 0,
        OffsetY = 6,
        Growth = StatusRowGrowth.Right,
        MaxIcons = 12
    };

    /// <summary>Enhancements go at the left of the bar and grow to the left.</summary>
    public static StatusRowSettings CreateEnhancements() => new();

    /// <summary>Enfeeblements go at the right of the bar and grow to the right.</summary>
    public static StatusRowSettings CreateEnfeeblements() => new()
    {
        BarAnchor = AnchorPoint.Right,
        IconAnchor = AnchorPoint.Left,
        OffsetX = 8,
        Growth = StatusRowGrowth.Right
    };
}

/// <summary>
/// The party list: one bar per member, one under the other. The look of each bar (colours, shield, name and HP text,
/// size of the first bar's box) is a normal <see cref="HudBarSettings"/>, so it is edited with the same controls as
/// the other HUD bars. What is only about the party is here.
/// </summary>
[Serializable]
public class PartyListSettings
{
    /// <summary>The look of one member's bar. Its size and position belong to the layout element of the party list.</summary>
    public HudBarSettings Look { get; set; } = CreateLook();

    /// <summary>Space between two bars, in pixels.</summary>
    public int RowSpacingPx { get; set; } = 6;

    public PartyMpMode MpMode { get; set; } = PartyMpMode.HealersAndRaisers;

    // The MP bar is a bar of its own, placed relative to the bar of the member. Where: a point of the MP bar (MpSelfAnchor) goes on a
    // point of the member's bar (MpBarAnchor), then the offsets move it (the saved Y counts downwards).
    public AnchorPoint MpBarAnchor { get; set; } = AnchorPoint.Bottom;

    public AnchorPoint MpSelfAnchor { get; set; } = AnchorPoint.Top;

    public int MpOffsetX { get; set; }

    public int MpOffsetY { get; set; } = 3;

    /// <summary>Width of the MP bar in pixels. 0 = as wide as the member's bar.</summary>
    public int MpWidthPx { get; set; }

    /// <summary>The rows start at the bottom of the box (which is always 9 rows tall: 8 members and the chocobo). Same idea as the game's "Align List to Bottom", but ours.</summary>
    public bool StartFromBottom { get; set; }

    /// <summary>The job icon of each member, inside the bar at its left. It works for every job, crafters and gatherers too.</summary>
    public bool ShowJobIcon { get; set; }

    public PartyJobIconStyle JobIconStyle { get; set; } = PartyJobIconStyle.Plain;

    /// <summary>Size of the icon in pixels; it never goes over the height of the bar.</summary>
    public int JobIconSizePx { get; set; } = 16;

    // Where the icon goes: a point of the icon (JobIconSelfAnchor) goes on a point of the member's bar (JobIconBarAnchor), then the offsets
    // move it (the saved Y counts downwards). By default: inside the bar, at the left, centered vertically.
    public AnchorPoint JobIconBarAnchor { get; set; } = AnchorPoint.Left;

    public AnchorPoint JobIconSelfAnchor { get; set; } = AnchorPoint.Left;

    public int JobIconOffsetX { get; set; } = 3;

    public int JobIconOffsetY { get; set; }

    /// <summary>The enhancements of each member, at the left of the bar by default.</summary>
    public StatusRowSettings Enhancements { get; set; } = StatusRowSettings.CreateEnhancements();

    /// <summary>The enfeeblements of each member, at the right of the bar by default.</summary>
    public StatusRowSettings Enfeeblements { get; set; } = StatusRowSettings.CreateEnfeeblements();

    /// <summary>The look the party bars start with: role colours, the name at the left and the percentage at the right.</summary>
    public static HudBarSettings CreateLook() => new()
    {
        MpHeightPx = 5,
        HpColorMode = HpColorMode.Role,
        HpPreset = HpTextPreset.Percent,
        Name = new FrameTextSettings { Anchor = AnchorPoint.Left, OffsetX = 10, OffsetY = 0, TextSize = 15 },
        Hp = new FrameTextSettings { Anchor = AnchorPoint.Right, OffsetX = -8, OffsetY = 0, TextSize = 14 }
    };
}
