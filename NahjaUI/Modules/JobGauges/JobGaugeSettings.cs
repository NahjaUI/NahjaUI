using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.JobGauges;

/// <summary>The settings of every job gauge (White Mage, Warrior and Paladin for now).</summary>
[Serializable]
public class JobGaugesSettings
{
    public WhiteMageGaugeSettings WhiteMage { get; set; } = new();

    public WarriorGaugeSettings Warrior { get; set; } = new();

    public PaladinGaugeSettings Paladin { get; set; } = new();

    public DarkKnightGaugeSettings DarkKnight { get; set; } = new();

    public GunbreakerGaugeSettings Gunbreaker { get; set; } = new();

    public RedMageGaugeSettings RedMage { get; set; } = new();

    public ScholarGaugeSettings Scholar { get; set; } = new();

    public AstrologianGaugeSettings Astrologian { get; set; } = new();

    public SageGaugeSettings Sage { get; set; } = new();

    /// <summary>The Thunderhead aura of the Black Mage (the rest of its gauge is the generic one, see Extra).</summary>
    public BlackMageAuraSettings BlackMage { get; set; } = new();

    /// <summary>The Sacred Sight aura of the White Mage.</summary>
    public WhiteMageAuraSettings WhiteMageAura { get; set; } = new();

    /// <summary>The jobs that use the generic gauge (melee, physical ranged and casters), by abbreviation.</summary>
    public ExtraGaugesSettings Extra { get; set; } = new();

    public LimitBreakGaugeSettings LimitBreak { get; set; } = new();

    /// <summary>The Limit Break gauge of PvP: its own bar, with its own position and look.</summary>
    public LimitBreakGaugeSettings LimitBreakPvp { get; set; } = new();

    public FrontlineGaugeSettings Frontline { get; set; } = new();
}

/// <summary>
/// The White Mage gauge: up to three lilies (one more every 20 seconds) and up to three blood lilies
/// (earned by healing with lilies, spent on Afflatus Misery). Position and size of each row belong to the layout system.
/// </summary>
[Serializable]
public class WhiteMageGaugeSettings
{
    public bool Enabled { get; set; } = true;

    public bool ShowBloodLilies { get; set; } = true;

    /// <summary>The seconds until the next lily, written on the lily that is filling up.</summary>
    public bool ShowTimer { get; set; } = true;

    /// <summary>Light up the blood lilies when all three are full (Afflatus Misery is ready).</summary>
    public bool GlowWhenFull { get; set; } = true;

    /// <summary>Space between pips for the rows that have no value of their own (configs saved before each row had its own). Not shown in the editor.</summary>
    public int GapPx { get; set; } = 3;

    /// <summary>Space between the lilies, in pixels. Null until the user chooses one (then the old shared value is used).</summary>
    public int? LilyGapPx { get; set; }

    /// <summary>Space between the blood lilies, in pixels. Null until the user chooses one (then the old shared value is used).</summary>
    public int? BloodLilyGapPx { get; set; }

    /// <summary>The number or timer on the gauge: place on the bar, offsets, size, font and color (same controls as every other text).</summary>
    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 8 };

    public Vector4 LilyColor { get; set; } = new(0.62f, 0.86f, 1f, 1f);

    public Vector4 BloodLilyColor { get; set; } = new(0.95f, 0.35f, 0.55f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }

    public Vector4 GlowColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;
}

/// <summary>
/// A gauge that is one bar from 0 to 100 with a spender at 50 (Warrior's Beast Gauge, Paladin's Oath Gauge). The mark at
/// the halfway point is where one spender can be used. Position and size belong to the layout system.
/// </summary>
[Serializable]
public class BarGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Write the number (0 to 100) on the bar.</summary>
    public bool ShowNumber { get; set; } = true;

    /// <summary>Draw a thin mark at 50, where one spender can be used.</summary>
    public bool ShowThreshold { get; set; } = true;

    /// <summary>Change colour while there is enough gauge (50 or more) for a spender.</summary>
    public bool HighlightWhenReady { get; set; } = true;



    /// <summary>The number or timer on the gauge: place on the bar, offsets, size, font and color (same controls as every other text).</summary>
    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 9 };

    public Vector4 BarColor { get; set; } = new(0.85f, 0.25f, 0.22f, 1f);

    public Vector4 ReadyColor { get; set; } = new(1f, 0.62f, 0.20f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }

    public Vector4 ThresholdColor { get; set; } = new(0f, 0f, 0f, 0.9f);
}

/// <summary>
/// The Warrior gauge: the Beast Gauge (Fell Cleave and Decimation cost 50), the glow of the gauge while Nascent Chaos is on, the Inner Release
/// pips, the Surging Tempest bar and the auras of Primal Rend and Primal Ruination.
/// </summary>
[Serializable]
public class WarriorGaugeSettings : BarGaugeSettings
{
    // Orange while there is not enough gauge for a spender and red when there is (the same order as the auras: orange comes before red).
    public WarriorGaugeSettings()
    {
        BarColor = new Vector4(1f, 0.62f, 0.20f, 1f);
        ReadyColor = new Vector4(0.85f, 0.25f, 0.22f, 1f);
    }

    /// <summary>Flash the fill of the Beast Gauge to another color and back while Nascent Chaos is on (the next Fell Cleave is Inner Chaos).</summary>
    public bool ShowNascentChaosFlash { get; set; } = true;

    public Vector4 NascentChaosFlashColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>Three pips, one for each stack of Inner Release.</summary>
    public bool ShowInnerRelease { get; set; } = true;

    public int InnerReleaseGapPx { get; set; } = 2;

    public Vector4 InnerReleaseColor { get; set; } = new(1f, 0.45f, 0.15f, 1f);

    /// <summary>A bar for the time left of Surging Tempest (60 seconds when full).</summary>
    public bool ShowSurgingTempest { get; set; } = true;

    public bool ShowSurgingTempestSeconds { get; set; } = true;

    public FrameTextSettings SurgingTempestText { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 9 };

    public Vector4 SurgingTempestColor { get; set; } = new(0.75f, 0.14f, 0.34f, 1f);

    /// <summary>The wings on both sides of the character while Primal Rend or Primal Ruination can be used.</summary>
    public bool ShowProcAuras { get; set; } = true;

    public float ProcAuraOpacity { get; set; } = 1f;

    /// <summary>The arch above the character while Wrathful is on (Primal Wrath can be used).</summary>
    public bool ShowWrathAura { get; set; } = true;

    public float WrathAuraOpacity { get; set; } = 1f;

    /// <summary>The steps of the combo you are in: three pips after Heavy Swing, two after Overpower. The last pip pulses: it is the next hit.</summary>
    public bool ShowCombo { get; set; } = true;

    /// <summary>Show the empty pips while there is no combo (the branch is not known until the first hit).</summary>
    public bool ShowComboOutline { get; set; }

    public int ComboGapPx { get; set; } = 2;

    public Vector4 ComboColor { get; set; } = new(0.93f, 0.74f, 0.30f, 1f);

    /// <summary>The last pip takes the refresh color while Surging Tempest has less than this many seconds (0 = off).</summary>
    public int ComboRefreshSeconds { get; set; }

    public Vector4 ComboRefreshColor { get; set; } = new(0.92f, 0.22f, 0.16f, 1f);
}

/// <summary>The aura of the White Mage: two flame arcs at both sides of the character while Sacred Sight is on (Glare IV x3 after Presence of Mind). Its sigils are the stacks left. No timer.</summary>
[Serializable]
public class WhiteMageAuraSettings
{
    public bool ShowSacredSightAura { get; set; } = true;

    public float SacredSightAuraOpacity { get; set; } = 1f;

    /// <summary>The arch of mist above the character while Freecure is on (the next Cure II costs no MP). No timer.</summary>
    public bool ShowFreeCureAura { get; set; } = true;

    public float FreeCureAuraOpacity { get; set; } = 1f;
}

/// <summary>The aura of the Black Mage: an arch above the character while Thunderhead is on (the next Thunder is instant). It has no timer: it is there or it is not.</summary>
[Serializable]
public class BlackMageAuraSettings
{
    public bool ShowThunderheadAura { get; set; } = true;

    public float ThunderheadAuraOpacity { get; set; } = 1f;

    /// <summary>The two fire arcs at both sides of the character while Firestarter is on (the next Fire III is instant and free). No timer.</summary>
    public bool ShowFirestarterAura { get; set; } = true;

    public float FirestarterAuraOpacity { get; set; } = 1f;

    /// <summary>The two ice and fire spirals at both sides of the character while the Paradox marker of the gauge is on (Paradox can be cast). No timer.</summary>
    public bool ShowParadoxAura { get; set; } = true;

    public float ParadoxAuraOpacity { get; set; } = 1f;
}

/// <summary>The Paladin gauge: the Oath Gauge. Holy Sheltron and Intervention cost 50.</summary>
[Serializable]
public class PaladinGaugeSettings : BarGaugeSettings
{
    public PaladinGaugeSettings()
    {
        BarColor = new Vector4(0.30f, 0.52f, 0.95f, 1f);
        ReadyColor = new Vector4(0.72f, 0.86f, 1f, 1f);
    }
}

/// <summary>
/// The Dark Knight gauge: Blood (a bar like the Warrior's, Bloodspiller and Living Shadow cost 50) and, under it, the Darkside time
/// left (a bar that empties, 60 seconds at most). While Dark Arts is active the Darkside bar gets a frame.
/// </summary>
[Serializable]
public class DarkKnightGaugeSettings : BarGaugeSettings
{
    public DarkKnightGaugeSettings()
    {
        BarColor = new Vector4(0.72f, 0.14f, 0.34f, 1f);
        ReadyColor = new Vector4(1f, 0.42f, 0.55f, 1f);
    }

    /// <summary>Show the Darkside time bar.</summary>
    public bool ShowDarkside { get; set; } = true;

    /// <summary>Write the seconds left on the Darkside bar.</summary>
    public bool ShowDarksideSeconds { get; set; } = true;

    /// <summary>Write the seconds left of Living Shadow on the Darkside bar (the game shows them next to its Darkside gauge).</summary>
    public bool ShowLivingShadow { get; set; } = true;

    /// <summary>The Living Shadow seconds: placed on the right of the Darkside bar by default.</summary>
    public FrameTextSettings LivingShadowText { get; set; } = new() { Anchor = AnchorPoint.Right, OffsetX = -6, TextSize = 9, CustomColor = true, Color = new Vector4(0.90f, 0.55f, 1f, 1f) };

    /// <summary>The seconds of Darkside on its bar: its own place, size, offsets and color (the number of the Blood bar is <c>Text</c>).</summary>
    public FrameTextSettings DarksideText { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 9 };

    /// <summary>Frame the Darkside bar while Dark Arts is active.</summary>
    public bool ShowDarkArts { get; set; } = true;

    public Vector4 DarksideColor { get; set; } = new(0.55f, 0.32f, 0.88f, 1f);

    public Vector4 DarkArtsColor { get; set; } = new(0.90f, 0.55f, 1f, 1f);
}

/// <summary>The Gunbreaker gauge: the Cartridges, up to three pips side by side.</summary>
[Serializable]
public class GunbreakerGaugeSettings
{
    public bool Enabled { get; set; } = true;

    public int GapPx { get; set; } = 3;



    public Vector4 CartridgeColor { get; set; } = new(0.95f, 0.62f, 0.20f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }
}

/// <summary>The Scholar gauge: the Aetherflow stacks (three pips) and the Faerie Gauge (a bar; while the Seraph is out it shows her time left).</summary>
[Serializable]
public class ScholarGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Write the Faerie Gauge value (or the seconds of the Seraph) on the bar.</summary>
    public bool ShowNumber { get; set; } = true;

    public int GapPx { get; set; } = 3;



    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 9 };

    public Vector4 AetherflowColor { get; set; } = new(0.45f, 0.85f, 0.55f, 1f);

    public Vector4 FaerieColor { get; set; } = new(0.55f, 0.95f, 0.85f, 1f);

    public Vector4 SeraphColor { get; set; } = new(1f, 0.88f, 0.55f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }
}

/// <summary>The Astrologian gauge: the three cards drawn (Astral or Umbral Draw) and the Crown card (Lord or Lady of Crowns).</summary>
[Serializable]
public class AstrologianGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Show the Crown card (Lord or Lady of Crowns).</summary>
    public bool ShowCrown { get; set; } = true;

    public int GapPx { get; set; } = 3;



    public Vector4 AstralColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;

    public Vector4 UmbralColor { get; set; } = new(0.45f, 0.62f, 1f, 1f);

    public Vector4 LordColor { get; set; } = new(0.95f, 0.35f, 0.35f, 1f);

    public Vector4 LadyColor { get; set; } = new(0.50f, 0.95f, 0.60f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }
}

/// <summary>The Sage gauge: the Addersgall stacks (one more every 20 seconds) and the Addersting stacks (earned by letting Addersgall overflow).</summary>
[Serializable]
public class SageGaugeSettings
{
    public bool Enabled { get; set; } = true;

    public bool ShowAddersting { get; set; } = true;

    /// <summary>The seconds until the next Addersgall, written on the stack that is filling up.</summary>
    public bool ShowTimer { get; set; } = true;

    public int GapPx { get; set; } = 3;



    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 9 };

    public Vector4 AddersgallColor { get; set; } = new(0.40f, 0.85f, 0.75f, 1f);

    public Vector4 AdderstingColor { get; set; } = new(0.85f, 0.35f, 0.55f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }
}

/// <summary>
/// The Red Mage gauge: White Mana and Black Mana (two bars from 0 to 100) and the Mana Stacks (three pips).
/// The melee combo needs 50 of each. Position and size of each row belong to the layout system.
/// </summary>
[Serializable]
public class RedMageGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Write the numbers on the bars.</summary>
    public bool ShowNumbers { get; set; } = true;

    /// <summary>Draw a thin mark at 50 on each bar (the melee combo needs 50 of each).</summary>
    public bool ShowThreshold { get; set; } = true;

    /// <summary>Show the Mana Stacks (three pips).</summary>
    public bool ShowStacks { get; set; } = true;

    /// <summary>Frame both bars while each has 50 or more (the melee combo can start).</summary>
    public bool FrameWhenReady { get; set; } = true;

    public int GapPx { get; set; } = 3;



    /// <summary>The number or timer on the gauge: place on the bar, offsets, size, font and color (same controls as every other text).</summary>
    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 8 };

    /// <summary>Off: the Black Mana bar writes its number like the White Mana one (the text above). On: it has its own text (place, size, color).</summary>
    public bool SeparateBlackText { get; set; }

    /// <summary>The number of the Black Mana bar, used only while <see cref="SeparateBlackText"/> is on.</summary>
    public FrameTextSettings BlackText { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 8 };

    public Vector4 WhiteColor { get; set; } = new(0.93f, 0.86f, 0.80f, 1f);

    public Vector4 BlackColor { get; set; } = new(0.36f, 0.30f, 0.80f, 1f);

    public Vector4 StackColor { get; set; } = new(0.95f, 0.45f, 0.45f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }

    public Vector4 ThresholdColor { get; set; } = new(0f, 0f, 0f, 0.9f);

    public Vector4 ReadyFrameColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;
}

/// <summary>
/// The Limit Break gauge of the party (the same for every job): up to three bars side by side, each one filling from the left.
/// A full bar is a Limit Break level. Position and size belong to the layout system.
/// </summary>
[Serializable]
public class LimitBreakGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Write the level (LB1, LB2, LB3) over the gauge while at least one bar is full.</summary>
    public bool ShowLevel { get; set; } = true;

    public int GapPx { get; set; } = 3;



    /// <summary>The number or timer on the gauge: place on the bar, offsets, size, font and color (same controls as every other text).</summary>
    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 8 };

    /// <summary>The bar that is filling up.</summary>
    public Vector4 FillColor { get; set; } = new(0.95f, 0.55f, 0.20f, 1f);

    /// <summary>A full bar (a level you can use).</summary>
    public Vector4 FullColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;

    /// <summary>The border of a full bar: it shows at a glance that the level can be used. White, like the border of a selected frame.</summary>
    public Vector4 ReadyFrameColor { get; set; } = new(1f, 1f, 1f, 1f);

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }
}

/// <summary>The gauge of the Frontline window of the game (PvP): one bar from 0 to its maximum, with the number on it.</summary>
[Serializable]
public class FrontlineGaugeSettings : BarGaugeSettings
{
    public FrontlineGaugeSettings()
    {
        // There is no spender at 50 here.
        ShowThreshold = false;
        HighlightWhenReady = false;
        // The same orange as the filling bar of the Limit Break.
        BarColor = new Vector4(0.95f, 0.55f, 0.20f, 1f);
    }
}

/// <summary>The settings of the generic gauges: one entry for each job, created the first time it is needed.</summary>
[Serializable]
public class ExtraGaugesSettings
{
    public System.Collections.Generic.Dictionary<string, ExtraGaugeJobSettings> Jobs { get; set; } = new();

    public ExtraGaugeJobSettings For(string abbreviation)
    {
        if (!Jobs.TryGetValue(abbreviation, out var settings))
        {
            settings = new ExtraGaugeJobSettings();
            Jobs[abbreviation] = settings;
        }

        return settings;
    }
}

/// <summary>What the user can change in one row of a generic gauge: to show it and its colours (the ones that were not changed follow the default).</summary>
[Serializable]
public class ExtraPartSettings
{
    public bool Show { get; set; } = true;

    /// <summary>Space between the pips or slots of this row, in pixels. Null while the user has not chosen one (the job's old value is used, 3 by default).</summary>
    public int? GapPx { get; set; }

    /// <summary>The colours the user changed, by index of the row's list of colours.</summary>
    public System.Collections.Generic.Dictionary<int, Vector4> Colors { get; set; } = new();
}

/// <summary>The settings of the generic gauge of one job. Position and size of each row belong to the layout system.</summary>
[Serializable]
public class ExtraGaugeJobSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Space between pips or slots for the rows that have no value of their own (configs saved before each row had its own). Not shown in the editor any more.</summary>
    public int GapPx { get; set; } = 3;


    /// <summary>The numbers and seconds written on the rows: place, offsets, size, font and colour (same controls as every other text).</summary>
    public FrameTextSettings Text { get; set; } = new() { Anchor = AnchorPoint.Center, TextSize = 8 };

    private Vector4 backgroundColorValue = NahjaUI.Core.Ui.Theme.BarBackground;
    public bool CustomBackground { get; set; }

    // The background color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BackgroundColor
    {
        get => CustomBackground ? backgroundColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Background;
        set => backgroundColorValue = value;
    }

    private Vector4 borderColorValue = NahjaUI.Core.Ui.Theme.BarBorder;
    public bool CustomBorder { get; set; }

    // The border color of Appearance, unless "Custom" is on. What is saved is the color in use.
    public Vector4 BorderColor
    {
        get => CustomBorder ? borderColorValue : NahjaUI.Core.Ui.GlobalColors.Current.Border;
        set => borderColorValue = value;
    }

    public Vector4 MarkColor { get; set; } = new(0f, 0f, 0f, 0.9f);

    public System.Collections.Generic.Dictionary<string, ExtraPartSettings> Parts { get; set; } = new();

    public ExtraPartSettings Part(string id)
    {
        if (!Parts.TryGetValue(id, out var part))
        {
            part = new ExtraPartSettings();
            Parts[id] = part;
        }

        return part;
    }

    /// <summary>The colour of a row: the one the user chose or, when they did not, the default of the job's definition.</summary>
    internal Vector4 ColorOf(GaugePartDef def, int index)
    {
        index = Math.Clamp(index, 0, def.Colors.Length - 1);
        return Part(def.Id).Colors.TryGetValue(index, out var color) ? color : def.Colors[index];
    }
}
