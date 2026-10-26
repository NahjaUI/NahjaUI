using System;

namespace NahjaUI.Modules.NamePlates;

/// <summary>
/// Settings of the nameplates. First step: only enemies, only a life bar and a name. Turned off until the owner switches it on
/// with "/nui nameplates".
/// </summary>
[Serializable]
public class NamePlateSettings
{
    public bool Enabled { get; set; } = false;

    // Hide the game's own plate of the enemies we draw.
    public bool HideNative { get; set; } = true;

    // Left click on a plate targets the enemy; right click opens the small NahjaUI menu (like the unit frames).
    public bool Clickable { get; set; } = true;

    // Size of the life bar, in logical pixels.
    public int WidthPx { get; set; } = 140;
    public int HeightPx { get; set; } = 14;
    public int RoundingPx { get; set; } = 4;

    // The name sits on the top left of the bar and touches its top edge, like in the unit frames.
    public int NameSize { get; set; } = 14;
    public int NameOffsetX { get; set; } = 4;
    public int NameOffsetY { get; set; } = -9;
    public bool ShowLevel { get; set; } = true;

    // The cast frame under the life bar: same width, the spell icon inside at its left.
    public bool ShowCast { get; set; } = true;
    public int CastHeightPx { get; set; } = 15;
    public int CastGapPx { get; set; } = 3;
    public int CastTextSize { get; set; } = 12;

    // Hide the game's own cast bars of enemies (addon "CastBarEnemy").
    public bool HideNativeCast { get; set; } = true;
}
