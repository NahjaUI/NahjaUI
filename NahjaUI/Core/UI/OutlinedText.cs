using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The one place that draws text with a black outline, so every text of the plugin (unit frames, auras, party icons,
/// cast bars, job gauges) looks the same. The outline is ONE PHYSICAL pixel thick, whatever the UI scale, so the edges stay clean.
/// </summary>
internal static class OutlinedText
{
    private static readonly Vector2[] Offsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f),
        new(-1f, 0f), new(1f, 0f),
        new(-1f, 1f), new(0f, 1f), new(1f, 1f)
    };

    /// <summary>The outline shapes the font lab compares. Physical pixels, whatever the UI scale.</summary>
    public enum Style
    {
        None,
        Px1Cross,
        Px1Eight,
        Px15Twelve,
        Px2Sixteen
    }

    private static Vector2[] RingOffsets(float radius, int count)
    {
        var result = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var angle = MathF.PI * 2f * i / count;
            result[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        return result;
    }

    private static readonly Vector2[] CrossOffsets = { new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f) };
    private static readonly Vector2[] TwelveOffsets = RingOffsets(1.5f, 12);
    private static readonly Vector2[] SixteenOffsets = RingOffsets(2f, 16);

    /// <summary>Draws the text with the given outline shape (used by the font lab on the Debug page).</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 pos, string text, uint color, Style style)
    {
        var black = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f));

        var offsets = style switch
        {
            Style.None => Array.Empty<Vector2>(),
            Style.Px1Cross => CrossOffsets,
            Style.Px15Twelve => TwelveOffsets,
            Style.Px2Sixteen => SixteenOffsets,
            _ => Offsets
        };

        foreach (var offset in offsets)
            drawList.AddText(pos + offset, black, text);

        drawList.AddText(pos, color, text);
    }

    /// <summary>Draws the text at <paramref name="pos"/> in the given colour (an ImGui colour value), with a black outline.</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 pos, string text, uint color)
    {
        // One physical pixel: with a 200% UI scale that is half of a logical pixel.
        const float thickness = 1f;
        var black = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 1f));

        foreach (var offset in Offsets)
            drawList.AddText(pos + offset * thickness, black, text);

        drawList.AddText(pos, color, text);
    }
}
