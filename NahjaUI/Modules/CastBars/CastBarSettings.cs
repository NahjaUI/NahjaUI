using System;
using System.Numerics;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.CastBars;

/// <summary>How the time of a cast is written.</summary>
public enum CastTimeMode
{
    /// <summary>The seconds left: "1.4".</summary>
    Remaining,

    /// <summary>The seconds done over the total: "0.6 / 2.0".</summary>
    ElapsedOverTotal
}

/// <summary>The settings of one cast bar. Position and size belong to the layout system.</summary>
[Serializable]
public class CastBarSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hide the game's own cast bar, which this one replaces.</summary>
    public bool HideNative { get; set; } = true;

    /// <summary>The icon of the spell, inside the bar at its left.</summary>
    public bool ShowIcon { get; set; } = true;

    public bool ShowName { get; set; } = true;

    public bool ShowTime { get; set; } = true;

    public CastTimeMode TimeMode { get; set; } = CastTimeMode.Remaining;

    /// <summary>Font size of the name and the time, in pixels (a fixed step, see OverlayFonts.AvailableSizes).</summary>
    public int TextSize { get; set; } = 14;

    public OverlayFont Font { get; set; } = OverlayFont.NahjaDisplay;

    public Vector4 BarColor { get; set; }

    public Vector4 BackgroundColor { get; set; } = new(0.07f, 0.08f, 0.13f, 0.85f);

    public Vector4 BorderColor { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// Marks the point from which you can move without losing the cast (slidecasting). Your own bar only.
    /// </summary>
    public bool ShowSlideCast { get; set; } = true;

    /// <summary>
    /// How long before the end of the cast the slidecast window opens, in milliseconds.
    /// The game's own value is about half a second; a high ping can make the real window shorter.
    /// </summary>
    public int SlideCastMs { get; set; } = 500;

    public Vector4 SlideCastColor { get; set; } = new(1f, 0.82f, 0.30f, 1f);

    /// <summary>Colour of the bar while a cast cannot be interrupted (target bar only).</summary>
    public Vector4 UninterruptibleColor { get; set; } = new(0.62f, 0.62f, 0.70f, 1f);
}

/// <summary>The two cast bars.</summary>
[Serializable]
public class CastBarsSettings
{
    public CastBarSettings Player { get; set; } = new() { BarColor = new Vector4(0.55f, 0.49f, 1f, 1f) };

    public CastBarSettings Target { get; set; } = new() { BarColor = new Vector4(0.92f, 0.36f, 0.36f, 1f) };
}
