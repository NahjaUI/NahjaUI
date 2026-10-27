using System;
using System.Numerics;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Tooltip;

/// <summary>
/// The box that shows the description of a buff or debuff while the mouse is over its icon: where it appears and how it looks.
/// </summary>
[Serializable]
public class TooltipSettings
{
    public bool Enabled { get; set; } = true;

    // Where it appears: the buffs and debuffs rows of each frame.
    public bool PlayerBuffs { get; set; } = true;
    public bool PlayerDebuffs { get; set; } = true;
    public bool TargetBuffs { get; set; } = true;
    public bool TargetDebuffs { get; set; } = true;
    public bool FocusBuffs { get; set; } = true;
    public bool FocusDebuffs { get; set; } = true;
    public bool PartyBuffs { get; set; } = true;
    public bool PartyDebuffs { get; set; } = true;

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
    public bool Allows(string place, bool isBuff) => Enabled && (place, isBuff) switch
    {
        ("player", true) => PlayerBuffs,
        ("player", false) => PlayerDebuffs,
        ("target", true) => TargetBuffs,
        ("target", false) => TargetDebuffs,
        ("focus", true) => FocusBuffs,
        ("focus", false) => FocusDebuffs,
        ("party", true) => PartyBuffs,
        ("party", false) => PartyDebuffs,
        _ => false
    };
}
