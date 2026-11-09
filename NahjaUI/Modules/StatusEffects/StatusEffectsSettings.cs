using System;
using NahjaUI.Modules.PartyListBar;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// The two groups NahjaUI draws, whatever the game's own layout option says (one element, three groups or four groups):
/// every enhancement of the player together and every enfeeblement of the player together.
/// </summary>
public enum StatusGroupKind
{
    Enhancements,
    Enfeeblements
}

/// <summary>
/// One group of status icons: its own box (moved in unlock mode), how many icons go in a line and where they grow, and the
/// look of the icons (the same look settings as every row of status icons, without their filters: a group shows every status of its kind).
/// </summary>
[Serializable]
public class StatusGroupSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>The look of the icons, the time and the stack count. Its filters, anchors and growth are not used here.</summary>
    public StatusRowSettings Look { get; set; } = new();

    public static StatusGroupSettings Create(int perLine, int mostIcons) => new()
    {
        Look = new StatusRowSettings
        {
            IconWidth = 32,
            IconHeight = 24,
            SpacingPx = 3,
            MaxIcons = mostIcons,
            IconsPerLine = perLine,

            // Left: the icons are added from the right edge of the box.
            Growth = NahjaUI.Modules.PartyListBar.StatusRowGrowth.Left
        }
    };
}

/// <summary>The enhancements and enfeeblements of the player: NahjaUI draws them in two groups, each in its own box.</summary>
[Serializable]
public class StatusEffectsSettings
{
    /// <summary>NahjaUI draws the player's statuses and hides the game's own windows for them.</summary>
    public bool Enabled { get; set; }

    public StatusGroupSettings Enhancements { get; set; } = StatusGroupSettings.Create(10, 20);

    public StatusGroupSettings Enfeeblements { get; set; } = StatusGroupSettings.Create(10, 20);

    public StatusGroupSettings For(StatusGroupKind kind) => kind == StatusGroupKind.Enhancements ? Enhancements : Enfeeblements;
}
