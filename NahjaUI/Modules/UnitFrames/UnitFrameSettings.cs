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
    public int TextSize { get; set; } = 11;

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplay;

    public Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
}

/// <summary>What the number texts of a bar (HP, MP) need to know to turn a value into text.</summary>
internal interface IValueTextFormat
{
    HpTextPreset HpPreset { get; }
    string CustomTemplate { get; }
    string Separator { get; }
    int PercentDecimals { get; }
    int AbbreviationDecimals { get; }
    ThousandsSeparator Thousands { get; }
}

/// <summary>The MP text of the player: the same choices as the HP text.</summary>
[Serializable]
public class MpTextSettings : IValueTextFormat
{
    public FrameTextSettings Text { get; set; } = new() { Enabled = false, Anchor = AnchorPoint.Center };
    public HpTextPreset HpPreset { get; set; } = HpTextPreset.Current;
    public string CustomTemplate { get; set; } = "{curk} | {pct}";
    public string Separator { get; set; } = " | ";
    public int PercentDecimals { get; set; } = 0;
    public int AbbreviationDecimals { get; set; } = 0;
    public ThousandsSeparator Thousands { get; set; } = ThousandsSeparator.Comma;
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
/// The settings of one unit frame. Position and size of the HP bar belong to the layout system.
/// The defaults give the look of: name above the bar at the right, half over its edge; HP text inside the bar at the left, below.
/// </summary>
[Serializable]
public class UnitFrameSettings : IValueTextFormat
{
    public bool Enabled { get; set; } = true;

    /// <summary>The target: the player's look, mirrored (name at the top left, HP text at the bottom right), and no MP.</summary>
    public static UnitFrameSettings CreateTarget() => new()
    {
        ShowMp = false,
        Name = new FrameTextSettings { Anchor = AnchorPoint.TopLeft, OffsetX = 4, OffsetY = -9 },
        Hp = new FrameTextSettings { Anchor = AnchorPoint.BottomRight, OffsetX = -4, OffsetY = -2 }
    };

    /// <summary>The target of the target and the focus: a thin bar with the name inside at the left and the percentage at the right.</summary>
    public static UnitFrameSettings CreateSmall() => new()
    {
        ShowMp = false,
        ShowShield = false,
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

    /// <summary>Corner rounding of the bar, in pixels.</summary>
    public int RoundingPx { get; set; } = 6;

    public Vector4 FriendlyColor { get; set; } = new(0.31f, 0.70f, 0.43f, 1f);

    /// <summary>Fixed colour for the HP, or the colour of the job.</summary>
    public HpColorMode HpColorMode { get; set; } = HpColorMode.Fixed;

    /// <summary>HP colour of a target that is a pet or a minion.</summary>
    public Vector4 PetColor { get; set; } = new(0.50f, 0.62f, 0.78f, 1f);

    /// <summary>Colour of a target that is an object: furniture, chest, aetheryte, event object...</summary>
    public Vector4 ObjectColor { get; set; } = new(0.55f, 0.57f, 0.62f, 1f);

    public Vector4 HostileColor { get; set; } = new(0.79f, 0.29f, 0.27f, 1f);

    public Vector4 BackgroundColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBackground;

    public Vector4 BorderColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBorder;

    // ---- shield
    public bool ShowShield { get; set; } = true;

    public Vector4 ShieldColor { get; set; } = new(0.96f, 0.91f, 0.66f, 1f);

    /// <summary>How solid the shield is (0 to 1). Below 1 the HP and the background show through it.</summary>
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

    /// <summary>Between the HP and the percentage in the presets that show both.</summary>
    public string Separator { get; set; } = " | ";

    /// <summary>Decimals of the percentage (0, 1 or 2).</summary>
    public int PercentDecimals { get; set; } = 0;

    /// <summary>Decimals of abbreviated numbers: 0 gives 738K, 1 gives 738.2K. Millions always show at least one.</summary>
    public int AbbreviationDecimals { get; set; } = 0;

    public ThousandsSeparator Thousands { get; set; } = ThousandsSeparator.Comma;

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

    /// <summary>Player only: the HP bar is drawn (the master switch of the Parameter bar page is Enabled).</summary>
    public bool ShowHp { get; set; } = true;

    public bool ParameterBarMigrated { get; set; }

    /// <summary>
    /// The page "Parameter bar" has one master switch (Enabled: ours is drawn and the game's is hidden) and then HP and MP
    /// each on or off. Before, Enabled was only the HP bar: someone with the HP bar off and the MP bar on keeps that.
    /// </summary>
    public void MigrateParameterBar()
    {
        if (ParameterBarMigrated)
            return;

        if (!Enabled && ShowMp)
        {
            Enabled = true;
            ShowHp = false;
        }

        ParameterBarMigrated = true;
    }

    /// <summary>The MP bar as a whole (its own bar, with its own position in the Layout).</summary>
    public bool ShowMp { get; set; } = true;

    /// <summary>The jobs with the MP bar on. By default the healers and the casters. (Crafters show CP and gatherers GP.)</summary>
    public List<uint> MpOnJobs { get; set; } = new() { 24, 28, 33, 40, 25, 27, 35, 42, 36 };

    public bool IsMpOn(uint jobId) => MpOnJobs.Contains(jobId);

    public void SetMpOn(uint jobId, bool on)
    {
        MpOnJobs.RemoveAll(existing => existing == jobId);
        if (on)
            MpOnJobs.Add(jobId);
    }

    // The old lists (MP under the HP bar / in its own box). Only read once to move the choices to MpOnJobs.
    public List<uint> MpJobs { get; set; } = new() { 24, 28, 33, 40, 25, 27, 35, 42, 36 };
    public List<uint> MpOwnBoxJobs { get; set; } = new();
    public bool MpMigrated { get; set; }

    public void MigrateMpLists()
    {
        if (MpMigrated)
            return;

        MpOnJobs = new List<uint>(MpJobs);
        foreach (var jobId in MpOwnBoxJobs)
        {
            if (!MpOnJobs.Contains(jobId))
                MpOnJobs.Add(jobId);
        }

        MpMigrated = true;
    }

    // How the MP bar looks (its size is in the Layout, like its position).
    public int MpRoundingPx { get; set; } = 6;

    /// <summary>The number on the MP bar (off by default).</summary>
    public MpTextSettings MpText { get; set; } = new();
    public Vector4 MpBackgroundColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBackground;
    public Vector4 MpBorderColor { get; set; } = NahjaUI.Core.Ui.Theme.BarBorder;

    public int MpHeightPx { get; set; } = 6;

    public int MpGapPx { get; set; } = 2;

    public Vector4 MpColor { get; set; } = new(0.36f, 0.55f, 0.94f, 1f);
}
