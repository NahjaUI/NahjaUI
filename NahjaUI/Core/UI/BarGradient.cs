using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The horizontal gradient of the filled part of bars and pips. The bar is drawn as always (rounded corners included) and then the colors of
/// its vertices are changed along X: from a slightly darker color with the hue moved one way to a slightly lighter one with the hue moved the other way.
/// So there is only one color to choose and the gradient comes from it. "Strength" 0 gives the flat color of before.
/// </summary>
internal static class BarGradient
{
    /// <summary>0 = flat, 1 = default gradient, 2 = twice as strong. Set by the plugin every frame from the Appearance page.</summary>
    public static float Strength { get; set; } = 1f;

    // How far the ends are from the chosen color at full strength: hue in degrees, brightness (0 to 1) on the HSV value.
    private const float LeftHue = -9f;
    private const float LeftValue = -0.10f;
    private const float RightHue = 10f;
    private const float RightValue = 0.12f;

    // The light end also gets a little white (more brightness alone does not show on a color that is already bright).
    private const float RightWhiteMix = 0.12f;

    // Blues need a bigger change of hue to show: the eye tells apart blue shades worse than others. 1 = same as the rest, around hue 225.
    private const float BlueHueBoost = 1.4f;
    private const float BlueCenterHue = 225f;
    private const float BlueSpreadHue = 45f;

    /// <summary>
    /// Draws a filled rectangle like AddRectFilled and gives it the gradient. The gradient goes from <paramref name="rangeStartX"/> to
    /// <paramref name="rangeEndX"/> (the part of the bar that is filled, or the span of all the pips), so the whole fill always shows it.
    /// </summary>
    public static unsafe void Fill(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float rounding, ImDrawFlags flags, float rangeStartX, float rangeEndX)
    {
        var mark = drawList.VtxBuffer.Size;
        drawList.AddRectFilled(min, max, color, rounding, flags);

        var span = rangeEndX - rangeStartX;
        if (Strength <= 0.001f || span < 1f || ((color >> 24) & 0xFFu) == 0)
            return;

        var vertices = drawList.VtxBuffer.Data;
        var count = drawList.VtxBuffer.Size;
        if (vertices == null || mark < 0 || mark >= count)
            return;

        var (left, right) = Ends(color);
        for (var i = mark; i < count; i++)
        {
            var t = Math.Clamp((vertices[i].Pos.X - rangeStartX) / span, 0f, 1f);
            var alpha = vertices[i].Col & 0xFF000000u;

            var r = (uint)MathF.Round(left.X + (right.X - left.X) * t);
            var g = (uint)MathF.Round(left.Y + (right.Y - left.Y) * t);
            var b = (uint)MathF.Round(left.Z + (right.Z - left.Z) * t);
            vertices[i].Col = alpha | (b << 16) | (g << 8) | r;
        }
    }

    // The two end colors (RGB 0 to 255) of a color in ImGui's packed format (the red is in the lowest byte).
    private static (Vector3 Left, Vector3 Right) Ends(uint color)
    {
        var r = (color & 0xFFu) / 255f;
        var g = ((color >> 8) & 0xFFu) / 255f;
        var b = ((color >> 16) & 0xFFu) / 255f;

        ToHsv(r, g, b, out var h, out var s, out var v);
        // Strength goes from 0 to 2 (the Appearance slider reaches 200 %): above 1 the gradient is stronger than the default.
        var k = Math.Clamp(Strength, 0f, 2f);

        var blue = Math.Clamp(1f - MathF.Abs(h - BlueCenterHue) / BlueSpreadHue, 0f, 1f);
        var hueScale = k * (1f + (BlueHueBoost - 1f) * blue);

        var left = FromHsv(h + LeftHue * hueScale, s, v + LeftValue * k);
        var right = FromHsv(h + RightHue * hueScale, s, v + RightValue * k);
        right = Vector3.Lerp(right, new Vector3(255f), Math.Clamp(RightWhiteMix * k, 0f, 0.5f));
        return (left, right);
    }

    private static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        v = max;
        s = max <= 0f ? 0f : delta / max;

        if (delta <= 0f)
            h = 0f;
        else if (max == r)
            h = 60f * (((g - b) / delta) % 6f);
        else if (max == g)
            h = 60f * ((b - r) / delta + 2f);
        else
            h = 60f * ((r - g) / delta + 4f);

        if (h < 0f)
            h += 360f;
    }

    private static Vector3 FromHsv(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        v = Math.Clamp(v, 0f, 1f);
        var c = v * s;
        var x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        var m = v - c;

        var (r, g, b) = h switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x)
        };

        return new Vector3((r + m) * 255f, (g + m) * 255f, (b + m) * 255f);
    }
}
