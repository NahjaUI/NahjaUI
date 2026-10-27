using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.NamePlates;

/// <summary>How the level is written after the name.</summary>
public enum NamePlateLevelMode
{
    /// <summary>Not written at all.</summary>
    Hidden,

    /// <summary>Only the number: "100".</summary>
    NumberOnly,

    /// <summary>With the prefix: "Lv 100".</summary>
    WithPrefix
}

/// <summary>
/// Settings of the nameplates. Enemies only for now. Turned off until it is switched on in the Nameplates page.
/// </summary>
[Serializable]
public class NamePlateSettings
{
    public bool Enabled { get; set; } = false;

    // Hide the game's own plate of the enemies we draw.
    public bool HideNative { get; set; } = true;

    // Left click on a plate targets the enemy; right click opens the small NahjaUI menu (like the unit frames).
    public bool Clickable { get; set; } = true;

    // Far enemies get a smaller plate, with the size the game gives each plate (it can change when you target an enemy).
    public bool ScaleWithDistance { get; set; } = false;

    // Size of the life bar, in logical pixels.
    public int WidthPx { get; set; } = 140;
    public int HeightPx { get; set; } = 14;
    public int RoundingPx { get; set; } = 4;

    // The name sits on the top left of the bar and touches its top edge, like in the unit frames.
    public bool ShowName { get; set; } = true;
    public AnchorPoint NameAnchor { get; set; } = AnchorPoint.TopLeft;
    public int NameSize { get; set; } = 14;
    public OverlayFont NameFont { get; set; } = OverlayFont.NahjaDisplay;
    public Vector4 NameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public int NameOffsetX { get; set; } = 4;
    public int NameOffsetY { get; set; } = -9;
    public Vector4 LevelColor { get; set; } = new(1f, 0.82f, 0.25f, 1f);
    public NamePlateLevelMode LevelMode { get; set; } = NamePlateLevelMode.NumberOnly;

    // Do not write the level of the enemies that have the same level as you.
    public bool HideSameLevel { get; set; } = false;

    // The cast frame under the life bar: same width, the spell icon inside at its left.
    public bool ShowCast { get; set; } = true;
    public int CastHeightPx { get; set; } = 15;
    public int CastGapPx { get; set; } = 3;
    public int CastTextSize { get; set; } = 12;
    public OverlayFont CastFont { get; set; } = OverlayFont.NahjaDisplay;

    // Hide the game's own cast bars of enemies (addon "CastBarEnemy").
    public bool HideNativeCast { get; set; } = true;
}
