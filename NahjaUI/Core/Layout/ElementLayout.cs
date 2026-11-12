using System;

namespace NahjaUI.Core.Layout;

/// <summary>
/// Where an element sits on screen. Everything is stored as whole numbers in logical pixels
/// (before the user's UI scale is applied), so positions are exact and reproducible.
///
/// Meaning: the point <see cref="SelfAnchor"/> of the element is placed at the point
/// <see cref="ScreenAnchor"/> of the screen, moved by (<see cref="OffsetX"/>, <see cref="OffsetY"/>).
/// </summary>
/// <summary>When an element is shown, with respect to combat.</summary>
public enum ShowCombat
{
    Always,
    InCombat,
    OutOfCombat
}

/// <summary>When an element is shown, with respect to the weapon.</summary>
public enum ShowWeapon
{
    Always,
    Drawn,
    Sheathed
}

[Serializable]
public class ElementLayout
{
    /// <summary>The id of another element this one is placed on (null = the screen). Then <see cref="ScreenAnchor"/> is a point of that element, not of the screen.</summary>
    public string? AttachTo { get; set; }

    public AnchorPoint ScreenAnchor { get; set; } = AnchorPoint.Center;
    public AnchorPoint SelfAnchor { get; set; } = AnchorPoint.Center;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; } = 100;
    public int Height { get; set; } = 30;
    public bool Locked { get; set; }

    /// <summary>Show only in combat or only out of combat (Always = no condition). Both conditions must be true. Not part of the position, so it is not saved per job.</summary>
    public ShowCombat ShowInCombat { get; set; } = ShowCombat.Always;

    /// <summary>Show only with the weapon drawn or only with it sheathed (Always = no condition).</summary>
    public ShowWeapon ShowWithWeapon { get; set; } = ShowWeapon.Always;

    /// <summary>True when the element has no condition at all.</summary>
    public bool HasNoCondition => ShowInCombat == ShowCombat.Always && ShowWithWeapon == ShowWeapon.Always;

    public ElementLayout Clone() => (ElementLayout)MemberwiseClone();

    /// <summary>Copies anchors, offsets and size from another layout. The lock state is kept.</summary>
    public void CopyPositionFrom(ElementLayout other)
    {
        AttachTo = other.AttachTo;
        ScreenAnchor = other.ScreenAnchor;
        SelfAnchor = other.SelfAnchor;
        OffsetX = other.OffsetX;
        OffsetY = other.OffsetY;
        Width = other.Width;
        Height = other.Height;
    }
}
