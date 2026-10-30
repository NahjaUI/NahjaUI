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

    /// <summary>The plate of the target always shows its HP bar, even when the game's options would show only the name (or nothing).</summary>
    public bool ShowBarOnTarget { get; set; } = true;

    // Test aid: write the flags the game gave each plate next to its name.
    public bool ShowGameFlags { get; set; } = false;

    // Size of the HP bar, in logical pixels.
    public int WidthPx { get; set; } = 140;
    public int HeightPx { get; set; } = 14;
    public int RoundingPx { get; set; } = 4;

    // The name sits on the top left of the bar and touches its top edge, like in the unit frames.
    public AnchorPoint NameAnchor { get; set; } = AnchorPoint.TopLeft;
    public int NameSize { get; set; } = 10;
    public OverlayFont NameFont { get; set; } = OverlayFont.NahjaDisplay;
    public int NameOffsetX { get; set; } = 4;
    public int NameOffsetY { get; set; } = -9;
    // The small icon the game puts at the left of the name (hostile, neutral, hunt rank...). The game decides which plates have one.
    public bool ShowNameIcon { get; set; } = true;
    public int NameIconSizePx { get; set; } = 16;
    public int NameIconGapPx { get; set; } = 3;

    // The big icon the game floats above the plate (quest marks and similar). Saved Y counts downward like the rest.
    public bool ShowMarkerIcon { get; set; } = true;
    public int MarkerIconSizePx { get; set; } = 32;
    public int MarkerOffsetX { get; set; } = 0;
    public int MarkerOffsetY { get; set; } = 6; // saved counts downward: the editor shows -6 (Sandro's tested value)

    // The color of the name and the title (the game's color option only colors the bar, so this one is ours).
    public Vector4 NameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 LevelColor { get; set; } = new(1f, 0.82f, 0.25f, 1f);
    public NamePlateLevelMode LevelMode { get; set; } = NamePlateLevelMode.NumberOnly;

    // Do not write the level of the enemies that have the same level as you.
    public bool HideSameLevel { get; set; } = false;

    // The cast frame under the HP bar: same width, the spell icon inside at its left.
    public bool ShowCast { get; set; } = true;
    public int CastHeightPx { get; set; } = 15;
    public int CastGapPx { get; set; } = 3;
    public int CastTextSize { get; set; } = 8;
    public OverlayFont CastFont { get; set; } = OverlayFont.NahjaDisplay;

}
