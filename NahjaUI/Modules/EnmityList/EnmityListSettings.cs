using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.PartyListBar;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.EnmityList;

/// <summary>The side of the bar where its cast goes.</summary>
public enum ProgressBarSide
{
    Below,
    Right,
    Left
}

/// <summary>Settings of the enmity list: one bar per enemy that is fighting you, like the game's own list (up to 8).</summary>
[Serializable]
public class EnmityListSettings
{
    public bool Enabled { get; set; } = false;

    public int HeightPx { get; set; } = 18;

    public int SpacingPx { get; set; } = 6;

    public Vector4 HpColor { get; set; } = new(0.79f, 0.29f, 0.27f, 1f);
    public Vector4 BackgroundColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBackground;
    public Vector4 BorderColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBorder;

    // The name and the HP percentage: the same kind of text as in the other frames (a point of the bar, offsets, size, font, color).
    // The name starts after the enmity dot (when it shows) and the percentage is attached to the whole bar.
    public FrameTextSettings Name { get; set; } = new()
    {
        Anchor = AnchorPoint.Left,
        OffsetX = 0,
        OffsetY = 0,
        TextSize = 14
    };

    public FrameTextSettings Hp { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = 0,
        TextSize = 14
    };

    /// <summary>A dot at the left of each bar: one color if the enemy is attacking you, another if it is attacking someone else.</summary>
    public bool ShowEnmityDot { get; set; } = true;

    /// <summary>The rows start at the bottom of the box (which is always 8 rows tall). The order of the enemies does not change.</summary>
    public bool StartFromBottom { get; set; }

    /// <summary>Diameter of the enmity dot, in logical pixels.</summary>
    public int EnmityDotSizePx { get; set; } = 12;

    /// <summary>The point of the bar the dot is attached to (the same point of the dot goes there), then the offsets move it.</summary>
    public AnchorPoint EnmityDotAnchor { get; set; } = AnchorPoint.Left;

    public int EnmityDotOffsetX { get; set; } = 8;

    /// <summary>Saved with Y counting downwards (the page shows plus as up), like the other offsets.</summary>
    public int EnmityDotOffsetY { get; set; } = 0;
    public Vector4 OnMeColor { get; set; } = new(0.90f, 0.25f, 0.25f, 1f);
    public Vector4 OnOthersColor { get; set; } = new(0.30f, 0.80f, 0.45f, 1f);

    // The cast of each enemy under its bar (shown when the game's own option says so). The color tells if you can interrupt it
    // (colors of the Progress bars page, target bar).
    public int ProgressBarHeightPx { get; set; } = 14;
    public int ProgressBarGapPx { get; set; } = 2;

    /// <summary>Which side of the bar the cast goes: under it (default), at its right or at its left.</summary>
    public ProgressBarSide ProgressBarPosition { get; set; } = ProgressBarSide.Below;

    /// <summary>Width of the cast, in logical pixels. 0 means the same width as the bar.</summary>
    public int ProgressBarWidthPx { get; set; } = 0;

    // Fine move of the cast (saved like the other offsets: Y counts downwards, the page shows plus as up).
    public int ProgressBarOffsetX { get; set; } = 0;
    public int ProgressBarOffsetY { get; set; } = 0;

    // Colors of the cast (they start like the target progress bar of the Progress bars page, but are our own): the fill says if it can be interrupted.
    public Vector4 ProgressBarColor { get; set; } = NahjaUI.Core.Ui.Theme.ProgressBarRed;
    public Vector4 ProgressBarUninterruptibleColor
    {
        get => progressBarUninterruptibleColor;
        // The old default was a grey: it is swapped once for the color of the player's progress bar.
        set => progressBarUninterruptibleColor = value == OldGrey ? PlayerViolet : value;
    }

    private static readonly Vector4 OldGrey = new(0.62f, 0.62f, 0.70f, 1f);
    private static readonly Vector4 PlayerViolet = NahjaUI.Core.Ui.Theme.ProgressBarViolet;
    private Vector4 progressBarUninterruptibleColor = PlayerViolet;
    public Vector4 ProgressBarBackgroundColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBackground;
    public Vector4 ProgressBarBorderColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBorder;

    // The rows of status icons of each enemy: the same rows as next to the other frames (filters, order, icons, texts). Both start off.
    // Enhancements go at the left of the bar and enfeeblements at the right; by default only your own enfeeblements (your DoTs).
    public StatusRowSettings Enhancements { get; set; } = CreateRow(true);

    public StatusRowSettings Enfeeblements { get; set; } = CreateRow(false);

    private static StatusRowSettings CreateRow(bool enhancements)
    {
        var row = enhancements ? StatusRowSettings.CreateEnhancements() : StatusRowSettings.CreateEnfeeblements();
        row.Enabled = false;
        row.IconWidth = 24;
        row.IconHeight = 18;
        row.MaxIcons = 6;
        row.TimerSize = 8;
        row.OnlyMine = !enhancements;
        return row;
    }

    // The action name (centred in the cast by default) and the time left (at the right). Offset Y -1 lifts them one pixel:
    // a text centred by its line height looks a little low.
    public FrameTextSettings ProgressBarName { get; set; } = new()
    {
        Anchor = AnchorPoint.Center,
        OffsetX = 0,
        OffsetY = -1,
        TextSize = 12
    };

    /// <summary>How the time of a cast is written (same choice as the progress bars).</summary>
    public NahjaUI.Modules.ProgressBars.ProgressBarTimeMode ProgressBarTimeMode { get; set; } = NahjaUI.Modules.ProgressBars.ProgressBarTimeMode.Remaining;

    public FrameTextSettings ProgressBarTime { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = -1,
        TextSize = 12
    };
}
