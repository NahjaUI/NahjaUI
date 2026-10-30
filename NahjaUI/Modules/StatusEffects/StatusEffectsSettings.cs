using System;
using NahjaUI.Modules.PartyFrames;

namespace NahjaUI.Modules.StatusEffects;

/// <summary>
/// The groups the game's own status element can be split into (HUD Layout > Status Effects). With one element the game shows
/// everything together; with three groups it shows enhancements, enfeeblements and others; with four it adds the conditional
/// enhancements (a small group of eight places).
/// </summary>
public enum StatusGroupKind
{
    Single,
    Enhancements,
    Conditional,
    Enfeeblements,
    Others
}

/// <summary>How the game's layout option is set right now: one element, three groups or four groups.</summary>
public enum StatusLayoutMode
{
    OneElement,
    ThreeGroups,
    FourGroups
}

/// <summary>Where a row of icons sits inside its box when it is not full.</summary>
public enum StatusJustify
{
    Left,
    Right,
    Center
}

/// <summary>
/// One group of status icons: its own box (moved in unlock mode), how many icons go in a row and how a row is justified, and the
/// look of the icons (the same look settings as every row of status icons, without their filters: a group shows what the game puts in it).
/// </summary>
[Serializable]
public class StatusGroupSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Icons in one row. More icons than this go to the next row.</summary>
    public int Columns { get; set; } = 10;

    public StatusJustify Justify { get; set; } = StatusJustify.Right;

    /// <summary>The next rows go above the first one instead of under it.</summary>
    public bool RowsGrowUp { get; set; }

    /// <summary>The look of the icons, the time and the stack count. Its filters, anchors and growth are not used here.</summary>
    public PartyAuraSettings Look { get; set; } = new();

    public static StatusGroupSettings Create(int columns, int mostIcons, StatusJustify justify) => new()
    {
        Columns = columns,
        Justify = justify,
        Look = new PartyAuraSettings
        {
            IconWidth = 32,
            IconHeight = 24,
            SpacingPx = 3,
            MaxIcons = mostIcons
        }
    };
}

/// <summary>The enhancements and enfeeblements of the player: NahjaUI draws them in the groups the game's layout option asks for.</summary>
[Serializable]
public class StatusEffectsSettings
{
    /// <summary>NahjaUI draws the player's statuses and hides the game's own windows for them.</summary>
    public bool Enabled { get; set; }

    public StatusGroupSettings Single { get; set; } = StatusGroupSettings.Create(10, 30, StatusJustify.Right);

    public StatusGroupSettings Enhancements { get; set; } = StatusGroupSettings.Create(10, 20, StatusJustify.Right);

    public StatusGroupSettings Conditional { get; set; } = StatusGroupSettings.Create(8, 8, StatusJustify.Left);

    public StatusGroupSettings Enfeeblements { get; set; } = StatusGroupSettings.Create(10, 20, StatusJustify.Right);

    public StatusGroupSettings Others { get; set; } = StatusGroupSettings.Create(10, 20, StatusJustify.Right);

    public StatusGroupSettings For(StatusGroupKind kind) => kind switch
    {
        StatusGroupKind.Enhancements => Enhancements,
        StatusGroupKind.Conditional => Conditional,
        StatusGroupKind.Enfeeblements => Enfeeblements,
        StatusGroupKind.Others => Others,
        _ => Single
    };
}
