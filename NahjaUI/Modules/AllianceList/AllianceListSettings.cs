using System;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.HudBars;
using NahjaUI.Modules.PartyListBar;

namespace NahjaUI.Modules.AllianceList;

/// <summary>
/// The alliance list: one grid with the members of the other parties of the alliance, side by side. The size of one cell and the
/// position of the grid belong to the layout element. The colors (role colors, background, border) come from the shared settings.
/// </summary>
[Serializable]
public class AllianceListSettings
{
    /// <summary>Draws NahjaUI's grid and hides the game's alliance lists.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Columns of the grid. The rows follow from it (and the other way round in the settings page).</summary>
    public int Columns { get; set; } = 4;

    /// <summary>Space between two cells, in pixels.</summary>
    public int SpacingPx { get; set; } = 4;

    /// <summary>The job icon of each member, inside the cell at its left.</summary>
    public bool ShowJobIcon { get; set; } = true;

    public PartyJobIconStyle JobIconStyle { get; set; } = PartyJobIconStyle.Plain;

    /// <summary>Size of the icon in pixels.</summary>
    public int JobIconSizePx { get; set; } = 16;

    // Where the icon goes, like in the party list: a point of the icon (JobIconSelfAnchor) goes on a point of the cell (JobIconBarAnchor),
    // then the offsets move it (the saved Y counts downwards).
    public AnchorPoint JobIconBarAnchor { get; set; } = AnchorPoint.Left;

    public AnchorPoint JobIconSelfAnchor { get; set; } = AnchorPoint.Left;

    public int JobIconOffsetX { get; set; } = 3;

    public int JobIconOffsetY { get; set; }

    /// <summary>How visible the cell of a member is while he is dead, offline, in another area or too far away (percent).</summary>
    public int AwayOpacityPercent { get; set; } = 40;

    /// <summary>"Dead" over the cell of a dead member (on or off, place, size, color). Members who are only away get no text. It ends in "..." if the cell is too narrow.</summary>
    public FrameTextSettings DeadText { get; set; } = new() { Anchor = AnchorPoint.Center, OffsetX = 0, OffsetY = 0, TextSize = 11 };

    /// <summary>The game's own Raise icon on a dead member who already has a Raise waiting for him.</summary>
    public bool ShowRaiseIcon { get; set; } = true;

    /// <summary>Size of the Raise icon, like the icons of the trackers: the game's icon is cut (never stretched) to the shape of this box.</summary>
    public int RaiseIconWidthPx { get; set; } = 16;

    public int RaiseIconHeightPx { get; set; } = 16;

    // Where it goes: a point of the icon on a point of the cell, then the offsets (the saved Y counts downwards).
    public AnchorPoint RaiseIconBarAnchor { get; set; } = AnchorPoint.TopRight;

    public AnchorPoint RaiseIconSelfAnchor { get; set; } = AnchorPoint.TopRight;

    public int RaiseIconOffsetX { get; set; } = -2;

    public int RaiseIconOffsetY { get; set; } = 2;

    /// <summary>The name of the member in a small box while the mouse is over his cell.</summary>
    public bool ShowNameOnHover { get; set; } = true;
}
