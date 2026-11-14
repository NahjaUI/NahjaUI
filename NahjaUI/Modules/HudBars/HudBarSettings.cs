using System;
using System.Collections.Generic;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.PartyListBar;
using NahjaUI.Modules.AllianceList;

namespace NahjaUI.Modules.HudBars;

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

/// <summary>What the HP text says. Custom uses the template written by the user.</summary>
public enum HpTextPreset
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

    /// <summary>-12K (what is missing; nothing when the HP is full)</summary>
    Deficit,

    Custom
}

/// <summary>How the HP bar is coloured.</summary>
public enum HpColorMode
{
    /// <summary>The same colour for everyone.</summary>
    Fixed,

    /// <summary>The colour of the role of the unit's job: tank, healer or damage (players only).</summary>
    Role
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
    public int TextSize { get; set; } = 11;

    // The text color of Appearance, unless "Custom color" is on. What is saved is the color in use.
    private Vector4 color = GlobalColors.DefaultText;

    public bool CustomColor { get; set; }

    public Vector4 Color
    {
        get => CustomColor ? color : GlobalColors.Current.Text;
        set => color = value;
    }
}

/// <summary>What the number texts of a bar (HP, MP) need to know to turn a value into text.</summary>
internal interface IValueTextFormat
{
    HpTextPreset HpPreset { get; }
    string CustomTemplate { get; }
    int PercentDecimals { get; }
    int AbbreviationDecimals { get; }
}

/// <summary>The MP text of the player: the same choices as the HP text.</summary>
[Serializable]
public class MpTextSettings : IValueTextFormat
{
    public FrameTextSettings Text { get; set; } = new() { Enabled = false, Anchor = AnchorPoint.Center };
    public HpTextPreset HpPreset { get; set; } = HpTextPreset.Current;
    public string CustomTemplate { get; set; } = "{curk} | {pct}";
    public int PercentDecimals { get; set; } = 0;
    public int AbbreviationDecimals { get; set; } = 0;
}

/// <summary>The settings of every HUD bar.</summary>
[Serializable]
public class HudBarsSettings
{
    public HudBarSettings Player { get; set; } = new();

    public HudBarSettings Target { get; set; } = HudBarSettings.CreateTarget();

    public HudBarSettings TargetOfTarget { get; set; } = HudBarSettings.CreateSmall();

    public HudBarSettings Focus { get; set; } = HudBarSettings.CreateSmall();

    /// <summary>The party list: one bar per member. Its look is a normal frame look, the rest is only about the party.</summary>
    public PartyListSettings Party { get; set; } = new();

    /// <summary>The alliance list: one grid with the members of the other parties of the alliance.</summary>
    public AllianceListSettings Alliance { get; set; } = new();

    /// <summary>The colour of each role, shared by every frame (the HP bars set to the colour of the role, and the stripe of the party).</summary>
    public Vector4 TankColor { get; set; } = new(0.31f, 0.55f, 0.88f, 1f);

    public Vector4 HealerColor { get; set; } = new(0.31f, 0.75f, 0.45f, 1f);

    public Vector4 DamageColor { get; set; } = new(0.85f, 0.33f, 0.31f, 1f);

    /// <summary>Disciples of the Hand (crafters) and of the Land (gatherers): the game's purple and gold, brightened like the role colors.</summary>
    public Vector4 CrafterColor { get; set; } = new(0.48f, 0.31f, 0.88f, 1f);

    public Vector4 GathererColor { get; set; } = new(0.88f, 0.74f, 0.31f, 1f);

    /// <summary>The colour of any other job without a role: grey, so it is never taken for a healer.</summary>
    public Vector4 NoRoleColor { get; set; } = new(0.55f, 0.57f, 0.62f, 1f);
}

/// <summary>
/// The settings of one HUD bar. Position and size of the HP bar belong to the layout system.
/// The defaults give the look of: name above the bar at the right, half over its edge; HP text inside the bar at the left, below.
/// </summary>
[Serializable]
public class HudBarSettings : IValueTextFormat
{
    public bool Enabled { get; set; } = true;

    /// <summary>The target: the player's look, mirrored (name at the top left, HP text at the bottom right), and no MP.</summary>
    public static HudBarSettings CreateTarget() => new()
    {
        Name = new FrameTextSettings { Anchor = AnchorPoint.TopLeft, OffsetX = 4, OffsetY = -9 },
        Hp = new FrameTextSettings { Anchor = AnchorPoint.BottomRight, OffsetX = -4, OffsetY = -2 }
    };

    /// <summary>The target of the target and the focus: a thin bar with the name inside at the left and the percentage at the right.</summary>
    public static HudBarSettings CreateSmall() => new()
    {
        HpPreset = HpTextPreset.Percent,
        Name = new FrameTextSettings { Anchor = AnchorPoint.Left, OffsetX = 4, OffsetY = 0, TextSize = 14 },
        Hp = new FrameTextSettings { Anchor = AnchorPoint.Right, OffsetX = -4, OffsetY = 0, TextSize = 14 }
    };

    /// <summary>The enhancements of the unit, in a row attached to the bar (player, target and focus frames). Off until switched on.</summary>
    public StatusRowSettings Enhancements { get; set; } = StatusRowSettings.CreateFrameEnhancements();

    /// <summary>The enfeeblements of the unit, in a row attached to the bar (player, target and focus frames). Off until switched on.</summary>
    public StatusRowSettings Enfeeblements { get; set; } = StatusRowSettings.CreateFrameEnfeeblements();

    /// <summary>Only used by the target of target: show its bar (background, HP, border) or just its name.</summary>
    public bool ShowFrame { get; set; } = true;



    /// <summary>Fixed colour for the HP, or the colour of the job.</summary>
    public HpColorMode HpColorMode { get; set; } = HpColorMode.Fixed;

    // ---- colors: all of them come from Appearance (the same for every bar). They are not saved here.
    [Newtonsoft.Json.JsonIgnore]
    public Vector4 BackgroundColor => GlobalColors.Current.Background;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 BorderColor => GlobalColors.Current.Border;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 ShieldColor => GlobalColors.Current.Shield;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 MpBackgroundColor => GlobalColors.Current.Background;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 MpBorderColor => GlobalColors.Current.Border;

    [Newtonsoft.Json.JsonIgnore]
    public Vector4 MpColor => GlobalColors.Current.Mp;

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

    // ---- HP text
    public FrameTextSettings Hp { get; set; } = new()
    {
        Anchor = AnchorPoint.BottomLeft,
        OffsetX = 4,
        OffsetY = -2
    };

    public HpTextPreset HpPreset { get; set; } = HpTextPreset.CurrentAndPercent;

    /// <summary>Used when the preset is Custom. Parts: {cur} {curk} {max} {maxk} {def} {defk} {pct}. The ones ending in k are abbreviated.</summary>
    public string CustomTemplate { get; set; } = "{curk} | {pct}";

    /// <summary>Decimals of the percentage (0, 1 or 2).</summary>
    public int PercentDecimals { get; set; } = 0;

    /// <summary>Decimals of abbreviated numbers: 0 gives 738K, 1 gives 738.2K. Millions always show at least one.</summary>
    public int AbbreviationDecimals { get; set; } = 0;

    // ---- HP bar and MP bar of the player: two separate things, each one on or off for every job

    /// <summary>The jobs (class job ids) with NO HP bar. Empty by default: every job, new ones included, has it.</summary>
    public List<uint> HideHpJobs { get; set; } = new();

    public bool IsHpOn(uint jobId) => !HideHpJobs.Contains(jobId);

    public void SetHpOn(uint jobId, bool on)
    {
        HideHpJobs.RemoveAll(existing => existing == jobId);
        if (!on)
            HideHpJobs.Add(jobId);
    }

    /// <summary>The jobs with the MP bar on. By default the healers, the casters, the Blue Mage, and all the crafters (8-15) and gatherers (16-18). (Crafters show CP and gatherers GP.)</summary>
    public List<uint> MpOnJobs { get; set; } = new() { 24, 28, 33, 40, 25, 27, 35, 42, 36, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

    public bool IsMpOn(uint jobId) => MpOnJobs.Contains(jobId);

    public void SetMpOn(uint jobId, bool on)
    {
        MpOnJobs.RemoveAll(existing => existing == jobId);
        if (on)
            MpOnJobs.Add(jobId);
    }

    /// <summary>The number on the MP bar (off by default).</summary>
    public MpTextSettings MpText { get; set; } = new();

    public int MpHeightPx { get; set; } = 6;

    public int MpGapPx { get; set; } = 2;
}
