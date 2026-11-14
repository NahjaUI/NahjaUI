using System;
using System.Numerics;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.ProgressBars;

/// <summary>How the time of a cast is written.</summary>
public enum ProgressBarTimeMode
{
    /// <summary>The seconds left: "1.4".</summary>
    Remaining,

    /// <summary>The seconds done over the total: "0.6 / 2.0".</summary>
    ElapsedOverTotal
}

/// <summary>The settings of one progress bar. Position and size belong to the layout system.</summary>
[Serializable]
public class ProgressBarSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>The icon of the action, inside the bar at its left.</summary>
    public bool ShowIcon { get; set; } = true;

    public ProgressBarTimeMode TimeMode { get; set; } = ProgressBarTimeMode.Remaining;

    // The action name and the time left, each with its own place, size, font and color (same controls as every other text).
    public FrameTextSettings Name { get; set; } = new()
    {
        Anchor = AnchorPoint.Left,
        OffsetX = 6,
        OffsetY = 0,
        TextSize = 10
    };

    public FrameTextSettings Time { get; set; } = new()
    {
        Anchor = AnchorPoint.Right,
        OffsetX = -6,
        OffsetY = 0,
        TextSize = 10
    };

    public Vector4 BarColor { get; set; }

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

    /// <summary>
    /// Marks the point from which you can move without losing the cast (slidecasting). Your own bar only.
    /// </summary>
    public bool ShowSlideCast { get; set; } = true;

    /// <summary>
    /// How long before the end of the cast the slidecast window opens, in milliseconds.
    /// The game's own value is about half a second; a high ping can make the real window shorter.
    /// </summary>
    public int SlideCastMs { get; set; } = 500;

    public Vector4 SlideCastColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;

    /// <summary>Colour of the bar while a cast cannot be interrupted (target bar only). It starts like the bar of the player.</summary>
    public Vector4 UninterruptibleColor
    {
        get => uninterruptibleColor;
        // The old default was a grey: it is swapped once for the color of the player's bar.
        set => uninterruptibleColor = value == OldGrey ? PlayerViolet : value;
    }

    private static readonly Vector4 OldGrey = new(0.62f, 0.62f, 0.70f, 1f);
    private static readonly Vector4 PlayerViolet = NahjaUI.Core.Ui.Theme.ProgressBarViolet;
    private Vector4 uninterruptibleColor = PlayerViolet;
}

/// <summary>The thin bar of the global cooldown, right above your progress bar.</summary>
[Serializable]
public class GcdBarSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>The bar stays visible (empty) when the global cooldown is not running.</summary>
    public bool AlwaysVisible { get; set; } = false;

    /// <summary>Marks the last part of the global cooldown, where the next action can already be queued.</summary>
    public bool ShowQueueWindow { get; set; } = true;

    /// <summary>How long before the end of the global cooldown the queue window opens (the game's own value is 500 ms).</summary>
    public int QueueMs { get; set; } = 500;

    /// <summary>Marks the point after which an ability would delay the next global action.</summary>
    public bool ShowWeaveWindow { get; set; } = true;

    /// <summary>How long before the end an ability would already delay the next action (the animation lock, about 700 ms).</summary>
    public int WeaveMs { get; set; } = 700;



    public Vector4 BarColor { get; set; } = new(0.85f, 0.85f, 0.92f, 1f);
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
    // The queue window is the same idea as the slidecast window (gold); the weave limit is a warning line (the red of the bars).
    public Vector4 QueueColor { get; set; } = NahjaUI.Core.Ui.Theme.Gold;
    public Vector4 WeaveColor { get; set; } = NahjaUI.Core.Ui.Theme.ProgressBarRed;
}

/// <summary>The progress bars: yours, the target's and the focus's (plus the global cooldown bar).</summary>
[Serializable]
public class ProgressBarsSettings
{
    public ProgressBarSettings Player { get; set; } = new() { BarColor = NahjaUI.Core.Ui.Theme.ProgressBarViolet };

    // The icon only shows when the target is an ally: the icons of enemy actions mean nothing.
    public GcdBarSettings Gcd { get; set; } = new();

    public ProgressBarSettings Target { get; set; } = new() { BarColor = NahjaUI.Core.Ui.Theme.ProgressBarRed };

    // Same as the target's: red while the cast can be interrupted, the violet of yours when it cannot. Off until the user turns it on.
    public ProgressBarSettings Focus { get; set; } = new() { Enabled = false, BarColor = NahjaUI.Core.Ui.Theme.ProgressBarRed };
}
