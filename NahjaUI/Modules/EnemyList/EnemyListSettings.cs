using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.EnemyList;

/// <summary>Settings of the enemy list: one bar per enemy that is fighting you, like the game's own list (up to 8).</summary>
[Serializable]
public class EnemyListSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Hide the game's own enemy list while ours is on.</summary>
    public bool HideNative { get; set; } = true;

    /// <summary>Left click on a bar targets the enemy; right click opens the game's menu (like the other frames).</summary>
    public bool Clickable { get; set; } = true;

    public int HeightPx { get; set; } = 18;
    public int RoundingPx { get; set; } = 4;
    public int SpacingPx { get; set; } = 6;

    public Vector4 LifeColor { get; set; } = new(0.79f, 0.29f, 0.27f, 1f);
    public Vector4 BackgroundColor { get; set; } = new(0.04f, 0.035f, 0.09f, 0.92f);
    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    public int TextSize { get; set; } = 14;
    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;
    public bool ShowPercent { get; set; } = true;

    /// <summary>A dot at the left of each bar: one color if the enemy is attacking you, another if it is attacking someone else.</summary>
    public bool ShowAggroDot { get; set; } = true;
    public Vector4 OnMeColor { get; set; } = new(0.90f, 0.25f, 0.25f, 1f);
    public Vector4 OnOthersColor { get; set; } = new(0.30f, 0.80f, 0.45f, 1f);

    /// <summary>The cast of each enemy under its bar. The color tells if you can interrupt it (colors of the Cast bars page, target bar).</summary>
    public bool ShowCast { get; set; } = true;
    public int CastHeightPx { get; set; } = 14;
    public int CastGapPx { get; set; } = 2;
}
