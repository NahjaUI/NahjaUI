using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.Filters;
using NahjaUI.Modules.PartyFrames;
using NahjaUI.Modules.UnitFrames;

namespace NahjaUI.Modules.NamePlates;

/// <summary>
/// How the plates of ONE kind of character look (the plates of the enemies, of the party...). The page has one of these per kind,
/// so the size, the progress bar or the status icons can be different for each.
/// </summary>
[Serializable]
public class NamePlateStyle
{
    /// <summary>Off: our plate is not drawn for this kind and the game's own plate stays as it is.</summary>
    public bool Active { get; set; } = true;

    // Size of the HP bar, in logical pixels.
    public int WidthPx { get; set; } = 140;
    public int HeightPx { get; set; } = 14;
    public int RoundingPx { get; set; } = 6;

    // The empty part and the outline of the HP bar (the filled part is the game's color).
    public Vector4 BarBackgroundColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBackground;
    public Vector4 BarBorderColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBorder;

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
    public Vector4 LevelColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;
    public NamePlateLevelMode LevelMode { get; set; } = NamePlateLevelMode.NumberOnly;

    // Do not write the level of the enemies that have the same level as you.
    public bool HideSameLevel { get; set; } = false;

    // The progress bar under the HP bar: same width as the bar.
    public bool ShowProgressBar { get; set; } = true;
    public int ProgressBarHeightPx { get; set; } = 15;
    public int ProgressBarGapPx { get; set; } = 3;

    // Same options as the casts of the enmity list: the side of the bar, the width (0 = the bar's) and a fine move.
    // Saved like the other offsets: Y counts downwards, the page shows plus as up.
    public NahjaUI.Modules.EnmityList.ProgressBarSide ProgressBarPosition { get; set; } = NahjaUI.Modules.EnmityList.ProgressBarSide.Below;
    public int ProgressBarWidthPx { get; set; } = 0;
    public int ProgressBarOffsetX { get; set; } = 0;
    public int ProgressBarOffsetY { get; set; } = 0;

    // Colors of the progress bar (they start like the target progress bar of the Progress bars page, but are our own): the fill says
    // if the cast can be interrupted.
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

    // The rows of status icons of a plate: enhancements above the bar, enfeeblements under it, placed like the ones of the unit frames.
    // They are the same rows as next to the unit frames (filters, order, icons, texts); both start off.
    public StatusRowSettings Enhancements { get; set; } = CreateRow(AnchorPoint.TopLeft, AnchorPoint.BottomLeft, 0, -14, StatusRowGrowth.Right);

    public StatusRowSettings Enfeeblements { get; set; } = CreateEnfeeblements();

    private static StatusRowSettings CreateRow(AnchorPoint barAnchor, AnchorPoint iconAnchor, int offsetX, int offsetY, StatusRowGrowth growth) => new()
    {
        Enabled = false,
        BarAnchor = barAnchor,
        IconAnchor = iconAnchor,
        OffsetX = offsetX,
        OffsetY = offsetY,
        Growth = growth,
        IconWidth = 24,
        IconHeight = 18,
        SpacingPx = 2,
        MaxIcons = 8,
        TimerSize = 8
    };

    private static StatusRowSettings CreateEnfeeblements()
    {
        var row = CreateRow(AnchorPoint.BottomLeft, AnchorPoint.TopLeft, 0, 6, StatusRowGrowth.Right);
        row.OnlyMine = true;
        return row;
    }

    // The action name (at the left) and the time left (at the right), each with its own place, size, font and color.
    public FrameTextSettings ProgressBarName { get; set; } = new()
    {
        Anchor = AnchorPoint.Left,
        OffsetX = 6,
        OffsetY = 0,
        TextSize = 8,
        Font = OverlayFont.NahjaDisplay
    };

    /// <summary>How the time of a cast is written (same choice as the progress bars).</summary>
    public NahjaUI.Modules.ProgressBars.ProgressBarTimeMode ProgressBarTimeMode { get; set; } = NahjaUI.Modules.ProgressBars.ProgressBarTimeMode.Remaining;

    public FrameTextSettings ProgressBarTime { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = 0,
        TextSize = 8,
        Font = OverlayFont.NahjaDisplay
    };

    /// <summary>The starting values of each kind of plate. Enemies show the level and the progress bar; the others start without them (they can be switched on).</summary>
    internal static NamePlateStyle Create(NamePlateRelation kind)
    {
        var style = new NamePlateStyle();
        var enemy = kind is NamePlateRelation.Enemy or NamePlateRelation.EnemyPlayer;

        // Enemies are drawn from the start; for the rest the game's own plate is a good start.
        style.Active = enemy;

        if (!enemy)
        {
            style.LevelMode = NamePlateLevelMode.Hidden;
            style.ShowProgressBar = false;
        }

        // The enfeeblements of the others are not "mine" only (the Esuna of an ally, for example, does not care who applied it).
        if (!enemy)
            style.Enfeeblements.OnlyMine = false;

        return style;
    }

    /// <summary>
    /// The starting row of the enemies' enfeeblements: the Crowd control filter first, then Mitigation (Feint, Addle, Reprisal, Dismantled:
    /// what makes the enemy hit softer), then Rotation upkeep, which only counts what I applied.
    /// </summary>
    internal static void ApplyEnemyEnfeeblements(StatusRowSettings row)
    {
        row.Enabled = true;
        row.Filter = StatusRowFilter.OnlyListed;
        row.StatusIds.Clear();
        row.OnlyMine = false;
        row.PriorityByFilter = true;
        row.FilterIds = new System.Collections.Generic.List<string>
        {
            FiltersSettings.CrowdControlId, DefensiveFilters.MitigationId, DefensiveFilters.RotationId
        };
        row.OnlyMineFilterIds = new System.Collections.Generic.List<string> { DefensiveFilters.RotationId };
    }
}
