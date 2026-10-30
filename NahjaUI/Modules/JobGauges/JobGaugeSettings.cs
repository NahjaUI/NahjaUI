using System;
using System.Numerics;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.JobGauges;

/// <summary>The settings of every job gauge (White Mage, Warrior and Paladin for now).</summary>
[Serializable]
public class JobGaugesSettings
{
    public WhiteMageGaugeSettings WhiteMage { get; set; } = new();

    public WarriorGaugeSettings Warrior { get; set; } = new();

    public PaladinGaugeSettings Paladin { get; set; } = new();
}

/// <summary>
/// The White Mage gauge: up to three lilies (one more every 20 seconds) and up to three blood lilies
/// (earned by healing with lilies, spent on Afflatus Misery). Position and size of each row belong to the layout system.
/// </summary>
[Serializable]
public class WhiteMageGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hide the game's own gauge, which this one replaces.</summary>
    public bool HideNative { get; set; } = true;

    public bool ShowBloodLilies { get; set; } = true;

    /// <summary>The seconds until the next lily, written on the lily that is filling up.</summary>
    public bool ShowTimer { get; set; } = true;

    /// <summary>Light up the blood lilies when all three are full (Afflatus Misery is ready).</summary>
    public bool GlowWhenFull { get; set; } = true;

    /// <summary>Space between two pips, in pixels.</summary>
    public int GapPx { get; set; } = 3;

    /// <summary>How round the corners of a pip are, in pixels.</summary>
    public int RoundingPx { get; set; } = 3;

    /// <summary>Font size of the timer, in pixels (a fixed step, see OverlayFonts.AvailableSizes).</summary>
    public int TextSize { get; set; } = 8;

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    public Vector4 LilyColor { get; set; } = new(0.62f, 0.86f, 1f, 1f);

    public Vector4 BloodLilyColor { get; set; } = new(0.95f, 0.35f, 0.55f, 1f);

    public Vector4 BackgroundColor { get; set; } = new(0.06f, 0.07f, 0.12f, 0.85f);

    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    public Vector4 GlowColor { get; set; } = new(1f, 0.82f, 0.30f, 1f);
}

/// <summary>
/// A gauge that is one bar from 0 to 100 with a spender at 50 (Warrior's Beast Gauge, Paladin's Oath Gauge). The mark at
/// the halfway point is where one spender can be used. Position and size belong to the layout system.
/// </summary>
[Serializable]
public class BarGaugeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hide the game's own gauge, which this one replaces.</summary>
    public bool HideNative { get; set; } = true;

    /// <summary>Write the number (0 to 100) on the bar.</summary>
    public bool ShowNumber { get; set; } = true;

    /// <summary>Draw a thin mark at 50, where one spender can be used.</summary>
    public bool ShowThreshold { get; set; } = true;

    /// <summary>Change colour while there is enough gauge (50 or more) for a spender.</summary>
    public bool HighlightWhenReady { get; set; } = true;

    /// <summary>How round the corners of the bar are, in pixels.</summary>
    public int RoundingPx { get; set; } = 3;

    public int TextSize { get; set; } = 8;

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplayBold;

    public Vector4 BarColor { get; set; } = new(0.85f, 0.25f, 0.22f, 1f);

    public Vector4 ReadyColor { get; set; } = new(1f, 0.62f, 0.20f, 1f);

    public Vector4 BackgroundColor { get; set; } = new(0.06f, 0.07f, 0.12f, 0.85f);

    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    public Vector4 ThresholdColor { get; set; } = new(0f, 0f, 0f, 0.9f);
}

/// <summary>The Warrior gauge: the Beast Gauge. Fell Cleave and Decimation cost 50.</summary>
[Serializable]
public class WarriorGaugeSettings : BarGaugeSettings
{
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
