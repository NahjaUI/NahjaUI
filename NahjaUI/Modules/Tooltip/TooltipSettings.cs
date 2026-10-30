using System;
using System.Numerics;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Tooltip;

/// <summary>
/// The box that shows the description of an enhancement or enfeeblement while the mouse is over its icon: where it appears and how it looks.
/// </summary>
[Serializable]
public class TooltipSettings
{
    public bool Enabled { get; set; } = true;

    // Where it appears: the enhancements and enfeeblements rows of each frame.
    public bool PlayerEnhancements { get; set; } = true;
    public bool PlayerEnfeeblements { get; set; } = true;
    public bool TargetEnhancements { get; set; } = true;
    public bool TargetEnfeeblements { get; set; } = true;
    public bool FocusEnhancements { get; set; } = true;
    public bool FocusEnfeeblements { get; set; } = true;
    public bool PartyEnhancements { get; set; } = true;
    public bool PartyEnfeeblements { get; set; } = true;

    // What it says.
    public bool ShowId { get; set; } = true;
    public bool ShowSource { get; set; } = true;

    // How it looks.
    public int TextSize { get; set; } = 14;
    public int MaxWidthPx { get; set; } = 320;
    public int RoundingPx { get; set; } = 6;

    /// <summary>The name uses the accent color of the interface unless this is off.</summary>
    public bool NameUsesAccent { get; set; } = true;
    public Vector4 NameColor { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 DescriptionColor { get; set; } = Theme.Text;
    public Vector4 DetailColor { get; set; } = Theme.TextMuted;
    public Vector4 BackgroundColor { get; set; } = new(0.063f, 0.071f, 0.118f, 0.96f);
    public Vector4 BorderColor { get; set; } = Theme.Border;

    /// <summary>Does the tooltip show for this row? place: "player", "target", "focus" or "party".</summary>
    public bool Allows(string place, bool isEnhancement) => Enabled && (place, isEnhancement) switch
    {
        ("player", true) => PlayerEnhancements,
        ("player", false) => PlayerEnfeeblements,
        ("target", true) => TargetEnhancements,
        ("target", false) => TargetEnfeeblements,
        ("focus", true) => FocusEnhancements,
        ("focus", false) => FocusEnfeeblements,
        ("party", true) => PartyEnhancements,
        ("party", false) => PartyEnfeeblements,
        _ => false
    };
}
