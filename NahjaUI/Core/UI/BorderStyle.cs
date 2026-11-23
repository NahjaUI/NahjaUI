using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The thickness of the borders, a setting of the Appearance page: one for bars (frames, progress bars, MP, job gauges, nameplate bars) and one for
/// icons (trackers and status icons). 1 is the border of always; 0 draws none. The text outline has no setting and is not here.
/// The border is drawn INSIDE the shape: a thicker one grows toward the center, so it never covers the neighbor (two pips with a separation of -1 share it).
/// </summary>
internal static class BorderStyle
{
    /// <summary>Thickness of the borders of bars in px (0 to 5). Set by the plugin every frame from the Appearance page.</summary>
    public static int BarPx { get; set; } = 1;

    /// <summary>Thickness of the borders of icons in px (0 to 5). Set by the plugin every frame from the Appearance page.</summary>
    public static int IconPx { get; set; } = 1;

    /// <summary>
    /// A border of a bar. <paramref name="baseThickness"/> is the one of always (1 px at the UI scale). A highlight (the target ring, a ready frame, a glow...)
    /// carries information, so it is still drawn when the setting is 0.
    /// </summary>
    public static void BarRect(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float rounding, ImDrawFlags flags, float baseThickness, bool highlight = false)
        => Rect(drawList, min, max, color, rounding, flags, baseThickness, BarPx, highlight);

    /// <summary>A border of an icon (see BarRect).</summary>
    public static void IconRect(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float rounding, ImDrawFlags flags, float baseThickness, bool highlight = false)
        => Rect(drawList, min, max, color, rounding, flags, baseThickness, IconPx, highlight);

    private static void Rect(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float rounding, ImDrawFlags flags, float baseThickness, int px, bool highlight)
    {
        var steps = highlight ? Math.Max(px, 1) : px;
        var thickness = baseThickness * steps;
        if (thickness <= 0f)
            return;

        // The extra thickness goes inward, so the outer edge stays where it was.
        var inset = MathF.Max(0f, (thickness - baseThickness) / 2f);
        drawList.AddRect(min + new Vector2(inset), max - new Vector2(inset), color, MathF.Max(0f, rounding - inset), flags, thickness);
    }
}
