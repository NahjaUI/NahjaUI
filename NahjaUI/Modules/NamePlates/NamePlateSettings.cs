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

    // Size of the life bar, in logical pixels.
    public int WidthPx { get; set; } = 140;
    public int HeightPx { get; set; } = 14;
    public int RoundingPx { get; set; } = 4;

    // The name sits on the top left of the bar and touches its top edge, like in the unit frames.
    public int NameSize { get; set; } = 14;
    public int NameOffsetX { get; set; } = 4;
    public int NameOffsetY { get; set; } = -9;
    public bool ShowLevel { get; set; } = true;
}
