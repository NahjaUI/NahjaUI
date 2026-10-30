using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The NahjaUI icon, drawn with plain shapes so it is always sharp at any size and follows the accent colour.
/// A shield made of four rounded tiles (the interface is made of modules) with two cat ears, for Nahja, a Miqo'te
/// Paladin. Two tones only; the inside of each ear has the other tone.
/// The same drawing, on a 128 x 128 grid, is in images/icon.svg (the picture used in the plugin list).
/// </summary>
internal static class NahjaMark
{
    /// <summary>Draws the icon in a square of <paramref name="size"/> pixels whose top left corner is <paramref name="pos"/>.</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 pos, float size, bool background = true)
    {
        // Everything is laid out on a 128 x 128 grid and scaled.
        var k = size / 128f;
        Vector2 P(float x, float y) => pos + new Vector2(x * k, y * k);

        // Only two tones, solid (no transparency, so nothing shows through where shapes meet): the accent and a darker one
        // (the accent over the dark tile). Top: bright left, dark right. Bottom: the other way round. The ears take the opposite tone
        // of the tile under them and their inside takes the opposite tone of the ear, so the icon is symmetric in shape and alternates in colour.
        var accent = Theme.Accent;
        var full = ImGui.GetColorU32(accent);
        var halfColor = background ? Theme.Mix(Theme.Card, accent, 0.55f) : Theme.WithAlpha(accent, 0.55f);
        var half = ImGui.GetColorU32(halfColor);
        var innerLeft = full;
        var innerRight = half;

        if (background)
            drawList.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.GetColorU32(Theme.Card), 28f * k);

        // The two top tiles.
        drawList.AddRectFilled(P(26f, 34f), P(61f, 65f), full, 7f * k);
        drawList.AddRectFilled(P(67f, 34f), P(102f, 65f), half, 7f * k);

        // The two bottom tiles, curved outwards so that together they make the point of a shield.
        drawList.PathClear();
        drawList.PathLineTo(P(33f, 71f));
        drawList.PathLineTo(P(61f, 71f));
        drawList.PathLineTo(P(61f, 105f));
        drawList.PathBezierCubicCurveTo(P(44f, 105f), P(26f, 92f), P(26f, 78f));
        drawList.PathBezierCubicCurveTo(P(26f, 74f), P(29f, 71f), P(33f, 71f));
        drawList.PathFillConvex(half);

        drawList.PathClear();
        drawList.PathLineTo(P(95f, 71f));
        drawList.PathLineTo(P(67f, 71f));
        drawList.PathLineTo(P(67f, 105f));
        drawList.PathBezierCubicCurveTo(P(84f, 105f), P(102f, 92f), P(102f, 78f));
        drawList.PathBezierCubicCurveTo(P(102f, 74f), P(99f, 71f), P(95f, 71f));
        drawList.PathFillConvex(full);

        // The ears, and the lighter inside of each one.
        drawList.AddTriangleFilled(P(26f, 36f), P(31f, 13f), P(51f, 36f), half);
        drawList.AddTriangleFilled(P(102f, 36f), P(97f, 13f), P(77f, 36f), full);
        drawList.AddTriangleFilled(P(32f, 33f), P(34f, 22f), P(43f, 33f), innerLeft);
        drawList.AddTriangleFilled(P(96f, 33f), P(94f, 22f), P(85f, 33f), innerRight);
    }
}
