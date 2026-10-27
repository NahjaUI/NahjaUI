using System;
using System.Collections.Generic;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.PartyFrames;

namespace NahjaUI.Modules.UnitFrames;

/// <summary>How a name is written. Players have a first and a last name; anything else is left as it is when "only players" is on.</summary>
public enum NameFormat
{
    /// <summary>Nahja Stark</summary>
    Full,

    /// <summary>Nahja</summary>
    FirstName,

    /// <summary>Stark</summary>
    LastName,

    /// <summary>N. Stark</summary>
    FirstInitialLastName,

    /// <summary>Nahja S.</summary>
    FirstNameLastInitial,

    /// <summary>N. S.</summary>
    Initials
}

/// <summary>What the life text says. Custom uses the template written by the user.</summary>
public enum HealthTextPreset
{
    /// <summary>738K | 100%</summary>
    CurrentAndPercent,

    /// <summary>738,000 | 100%</summary>
    CurrentFullAndPercent,

    /// <summary>738K</summary>
    Current,

    /// <summary>738,000</summary>
    CurrentFull,

    /// <summary>100%</summary>
    Percent,

    /// <summary>738K / 738K</summary>
    CurrentAndMax,

    /// <summary>-12K (what is missing; nothing when the life is full)</summary>
    Deficit,

    Custom
}

/// <summary>Where the mana bar of a job is drawn.</summary>
public enum ManaPlacement
{
    /// <summary>No mana bar on this job.</summary>
    Off,

    /// <summary>Under the life bar, as wide as it.</summary>
    UnderLifeBar,

    /// <summary>In its own box, which is moved in unlock mode (for example next to the job gauge).</summary>
    OwnBox
}

/// <summary>How the life bar is coloured.</summary>
public enum LifeColorMode
{
    /// <summary>The same colour for everyone.</summary>
    Fixed,

    /// <summary>The colour of the job of the unit (players only).</summary>
    Class
}

/// <summary>The character that separates the groups of three digits in long numbers.</summary>
public enum ThousandsSeparator
{
    /// <summary>104,071</summary>
    Comma,

    /// <summary>104.071</summary>
    Dot,

    /// <summary>104 071</summary>
    Space,

    /// <summary>104071</summary>
    None
}

/// <summary>
/// One text drawn over a frame. It is attached to a point of the bar (the anchor): the same point of the text is placed
/// on it, so a right anchor right-justifies the text and a centre anchor centres it. The offset then moves it, in pixels.
/// </summary>
[Serializable]
public class FrameTextSettings
{
    public bool Enabled { get; set; } = true;

    public AnchorPoint Anchor { get; set; } = AnchorPoint.Center;

    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>Font size in pixels (a fixed step, see OverlayFonts.AvailableSizes).</summary>
    public int TextSize { get; set; } = 16;

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplay;

    public Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
}

/// <summary>The settings of every unit frame.</summary>
[Serializable]
public class UnitFramesSettings
{
    public UnitFrameSettings Player { get; set; } = new();

    public UnitFrameSettings Target { get; set; } = UnitFrameSettings.CreateTarget();

    public UnitFrameSettings TargetOfTarget { get; set; } = UnitFrameSettings.CreateSmall();

    public UnitFrameSettings Focus { get; set; } = UnitFrameSettings.CreateSmall();

    /// <summary>The party frames: one bar per member. Its look is a normal frame look, the rest is only about the party.</summary>
    public PartyFrameSettings Party { get; set; } = new();

    /// <summary>The job colours the user changed (class job id to colour), shared by every frame. The others use the built-in ones, see JobColors.</summary>
    public Dictionary<uint, Vector4> JobColorOverrides { get; set; } = new();
}

/// <summary>
/// The settings of one unit frame. Position and size of the life bar belong to the layout system.
/// The defaults give the look of: name above the bar at the right, half over its edge; life text inside the bar at the left, below.
/// </summary>
[Serializable]
public class UnitFrameSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hide the game's own bar, which this one replaces. The target of target has none of its own: it is part of the target's bar.</summary>
    public bool HideNative { get; set; } = true;

    /// <summary>The target: the player's look, mirrored (name at the top left, life text at the bottom right), and no mana.</summary>
    public static UnitFrameSettings CreateTarget() => new()
    {
        HideNative = true,
        ShowMana = false,
        Name = new FrameTextSettings { Anchor = AnchorPoint.TopLeft, OffsetX = 4, OffsetY = -9 },
        Health = new FrameTextSettings { Anchor = AnchorPoint.BottomRight, OffsetX = -4, OffsetY = -2 }
    };

    /// <summary>The target of the target and the focus: a thin bar with the name inside at the left and the percentage at the right.</summary>
    public static UnitFrameSettings CreateSmall() => new()
    {
        HideNative = true,
        ShowMana = false,
        ShowShield = false,
        HealthPreset = HealthTextPreset.Percent,
        Name = new FrameTextSettings { Anchor = AnchorPoint.Left, OffsetX = 4, OffsetY = 0, TextSize = 14 },
        Health = new FrameTextSettings { Anchor = AnchorPoint.Right, OffsetX = -4, OffsetY = 0, TextSize = 14 }
    };

    /// <summary>The buffs of the unit, in a row attached to the bar (player, target and focus frames). Off until switched on.</summary>
    public PartyAuraSettings Buffs { get; set; } = PartyAuraSettings.CreateFrameBuffs();

    /// <summary>The debuffs of the unit, in a row attached to the bar (player, target and focus frames). Off until switched on.</summary>
    public PartyAuraSettings Debuffs { get; set; } = PartyAuraSettings.CreateFrameDebuffs();

    /// <summary>Target frame: hide the game's own buffs and debuffs of the target while one of our two rows is on.</summary>
    public bool HideNativeStatuses { get; set; } = true;

    /// <summary>Left click on the frame targets the unit, right click opens a small menu. When off, clicks go through to the game.</summary>
    public bool Clickable { get; set; } = true;

    /// <summary>Corner rounding of the bar, in pixels.</summary>
    public int RoundingPx { get; set; } = 3;

    public Vector4 FriendlyColor { get; set; } = new(0.31f, 0.70f, 0.43f, 1f);

    /// <summary>Fixed colour for the life, or the colour of the job.</summary>
    public LifeColorMode LifeColorMode { get; set; } = LifeColorMode.Fixed;

    /// <summary>Life colour of a target that is a pet or a minion.</summary>
    public Vector4 PetColor { get; set; } = new(0.50f, 0.62f, 0.78f, 1f);

    /// <summary>Colour of a target that is an object: furniture, chest, aetheryte, event object...</summary>
    public Vector4 ObjectColor { get; set; } = new(0.55f, 0.57f, 0.62f, 1f);

    public Vector4 HostileColor { get; set; } = new(0.79f, 0.29f, 0.27f, 1f);

    public Vector4 BackgroundColor { get; set; } = new(0.04f, 0.05f, 0.09f, 0.92f);

    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    // ---- shield
    public bool ShowShield { get; set; } = true;

    public Vector4 ShieldColor { get; set; } = new(0.96f, 0.91f, 0.66f, 1f);

    /// <summary>How solid the shield is (0 to 1). Below 1 the life and the background show through it.</summary>
    public float ShieldOpacity { get; set; } = 0.65f;

    // ---- name
    public FrameTextSettings Name { get; set; } = new()
    {
        Anchor = AnchorPoint.TopRight,
        OffsetX = -4,
        OffsetY = -9
    };

    public NameFormat NameFormat { get; set; } = NameFormat.Full;

    /// <summary>Apply the name format only to players, and leave the names of enemies and NPCs as they are.</summary>
    public bool NameFormatOnlyPlayers { get; set; } = true;

    // ---- life text
    public FrameTextSettings Health { get; set; } = new()
    {
        Anchor = AnchorPoint.BottomLeft,
        OffsetX = 4,
        OffsetY = -2
    };

    public HealthTextPreset HealthPreset { get; set; } = HealthTextPreset.CurrentAndPercent;

    /// <summary>Used when the preset is Custom. Parts: {cur} {curk} {max} {maxk} {def} {defk} {pct}. The ones ending in k are abbreviated.</summary>
    public string CustomTemplate { get; set; } = "{curk} | {pct}";

    /// <summary>Between the life and the percentage in the presets that show both.</summary>
    public string Separator { get; set; } = " | ";

    /// <summary>Decimals of the percentage (0, 1 or 2).</summary>
    public int PercentDecimals { get; set; } = 0;

    /// <summary>Decimals of abbreviated numbers: 0 gives 738K, 1 gives 738.2K. Millions always show at least one.</summary>
    public int AbbreviationDecimals { get; set; } = 0;

    public ThousandsSeparator Thousands { get; set; } = ThousandsSeparator.Comma;

    // ---- mana (the player's frame)
    public bool ShowMana { get; set; } = true;

    /// <summary>The jobs (class job ids) with the mana bar UNDER the life bar. By default the healers and casters.</summary>
    public List<uint> ManaJobs { get; set; } = new() { 24, 28, 33, 40, 25, 27, 35, 42, 36 };

    /// <summary>The jobs with the mana bar in its OWN box, which can be put anywhere (next to the job gauge, for example).</summary>
    public List<uint> ManaOwnBoxJobs { get; set; } = new();

    public ManaPlacement GetManaPlacement(uint jobId)
    {
        if (ManaOwnBoxJobs.Contains(jobId))
            return ManaPlacement.OwnBox;

        return ManaJobs.Contains(jobId) ? ManaPlacement.UnderLifeBar : ManaPlacement.Off;
    }

    /// <summary>A job is only ever in one of the two lists.</summary>
    public void SetManaPlacement(uint jobId, ManaPlacement placement)
    {
        ManaJobs.RemoveAll(existing => existing == jobId);
        ManaOwnBoxJobs.RemoveAll(existing => existing == jobId);

        if (placement == ManaPlacement.UnderLifeBar)
            ManaJobs.Add(jobId);
        else if (placement == ManaPlacement.OwnBox)
            ManaOwnBoxJobs.Add(jobId);
    }

    public int ManaHeightPx { get; set; } = 6;

    public int ManaGapPx { get; set; } = 2;

    public Vector4 ManaColor { get; set; } = new(0.36f, 0.55f, 0.94f, 1f);
}
