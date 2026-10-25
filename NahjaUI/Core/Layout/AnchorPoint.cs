using System;
using System.Numerics;

namespace NahjaUI.Core.Layout;

/// <summary>The nine anchor positions of a rectangle: corners, edge centres and centre.</summary>
public enum AnchorPoint
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

internal static class AnchorPointExtensions
{
    public static readonly AnchorPoint[] All = (AnchorPoint[])Enum.GetValues(typeof(AnchorPoint));

    /// <summary>Position of the anchor inside a rectangle, from (0,0) top-left to (1,1) bottom-right.</summary>
    public static Vector2 Fraction(this AnchorPoint point) => point switch
    {
        AnchorPoint.TopLeft => new Vector2(0f, 0f),
        AnchorPoint.Top => new Vector2(0.5f, 0f),
        AnchorPoint.TopRight => new Vector2(1f, 0f),
        AnchorPoint.Left => new Vector2(0f, 0.5f),
        AnchorPoint.Center => new Vector2(0.5f, 0.5f),
        AnchorPoint.Right => new Vector2(1f, 0.5f),
        AnchorPoint.BottomLeft => new Vector2(0f, 1f),
        AnchorPoint.Bottom => new Vector2(0.5f, 1f),
        AnchorPoint.BottomRight => new Vector2(1f, 1f),
        _ => new Vector2(0.5f, 0.5f)
    };

    public static string Label(this AnchorPoint point) => point switch
    {
        AnchorPoint.TopLeft => "Top left",
        AnchorPoint.Top => "Top",
        AnchorPoint.TopRight => "Top right",
        AnchorPoint.Left => "Left",
        AnchorPoint.Center => "Center",
        AnchorPoint.Right => "Right",
        AnchorPoint.BottomLeft => "Bottom left",
        AnchorPoint.Bottom => "Bottom",
        AnchorPoint.BottomRight => "Bottom right",
        _ => point.ToString()
    };
}
