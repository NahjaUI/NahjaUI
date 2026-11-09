using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The one place that draws text with a black outline, so every text of the plugin (HUD bars, trackers, party icons,
/// progress bars, job gauges) looks the same. The outline is ONE PHYSICAL pixel thick, whatever the UI scale, so the edges stay clean.
/// </summary>
internal static class OutlinedText
{
    private static readonly Vector2[] Offsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f),
        new(-1f, 0f), new(1f, 0f),
        new(-1f, 1f), new(0f, 1f), new(1f, 1f)
    };

    /// <summary>The same with an outline of the given color (an ImGui colour value).</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 pos, string text, uint color, uint outline)
    {
        foreach (var offset in Offsets)
            drawList.AddText(pos + offset, outline, text);

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
