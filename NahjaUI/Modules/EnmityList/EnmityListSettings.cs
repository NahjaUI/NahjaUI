using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.EnmityList;

/// <summary>The side of the bar where its cast goes.</summary>
public enum EnemyCastSide
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

    /// <summary>Test aid: write the game's values of each row (active, locked, targetable) at the right of the bar.</summary>
    public bool ShowGameValues { get; set; } = false;

    /// <summary>Test aid: do not hide the game's own enmity list, so it can be compared with ours (move ours in unlock mode).</summary>
    public bool ShowGameList { get; set; } = false;

    public int HeightPx { get; set; } = 18;
    public int RoundingPx { get; set; } = 4;
    public int SpacingPx { get; set; } = 6;

    public Vector4 HpColor { get; set; } = new(0.79f, 0.29f, 0.27f, 1f);
    public Vector4 BackgroundColor { get; set; } = new(0.04f, 0.035f, 0.09f, 0.92f);
    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    // The name and the HP percentage: the same kind of text as in the other frames (a point of the bar, offsets, size, font, color).
    // The name starts after the enmity dot (when it shows) and the percentage is attached to the whole bar.
    public FrameTextSettings Name { get; set; } = new()
    {
        Anchor = AnchorPoint.Left,
        OffsetX = 0,
        OffsetY = 0,
        TextSize = 14,
        Font = OverlayFont.NahjaDisplayBold
    };

    public FrameTextSettings Health { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = 0,
        TextSize = 14,
        Font = OverlayFont.NahjaDisplayBold
    };

    /// <summary>A dot at the left of each bar: one color if the enemy is attacking you, another if it is attacking someone else.</summary>
    public bool ShowEnmityDot { get; set; } = true;

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
    // (colors of the Cast bars page, target bar).
    public int CastHeightPx { get; set; } = 14;
    public int CastGapPx { get; set; } = 2;

    /// <summary>Which side of the bar the cast goes: under it (default), at its right or at its left.</summary>
    public EnemyCastSide CastSide { get; set; } = EnemyCastSide.Below;

    /// <summary>Width of the cast, in logical pixels. 0 means the same width as the bar.</summary>
    public int CastWidthPx { get; set; } = 0;

    // Fine move of the cast (saved like the other offsets: Y counts downwards, the page shows plus as up).
    public int CastOffsetX { get; set; } = 0;
    public int CastOffsetY { get; set; } = 0;

    // The spell name (centred in the cast by default) and the time left (at the right). Offset Y -1 lifts them one pixel:
    // a text centred by its line height looks a little low.
    public FrameTextSettings CastName { get; set; } = new()
    {
        Anchor = AnchorPoint.Center,
        OffsetX = 0,
        OffsetY = -1,
        TextSize = 12,
        Font = OverlayFont.NahjaDisplayBold
    };

    public FrameTextSettings CastTime { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = -1,
        TextSize = 12,
        Font = OverlayFont.NahjaDisplayBold
    };
}
