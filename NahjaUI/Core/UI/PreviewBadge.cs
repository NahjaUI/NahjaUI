using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The mark that tells that what is on screen is a preview of the settings page (made-up data), so it is never taken for real.
/// </summary>
internal static class PreviewBadge
{
    /// <summary>The height of the letters of the mark, in logical pixels (like the size of every text of the plugin: the fonts are built already scaled).</summary>
    private const float LetterHeightPx = 12f;

    /// <summary>Draws "Preview" just above <paramref name="boxTop"/> (the top left corner of the element).</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 boxTop)
    {
        const string text = "PREVIEW";
        var scale = UiScale.Overlay;
        var color = ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.25f, 0.9f));

        // The font of the plugin (Saira), drawn the same way as the other texts of NahjaUI.
        if (OverlayFonts.Current is { } fonts)
        {
            using var face = fonts.PushCap(OverlayFont.NahjaDisplay, LetterHeightPx);
            var letters = face.Measure(text);
            face.Draw(drawList, new Vector2(boxTop.X, boxTop.Y - letters.Y - 2f * scale), text, color);
            return;
        }

        // The fonts are not there yet: the default font of ImGui.
        var size = ImGui.CalcTextSize(text);
        OutlinedText.Draw(drawList, new Vector2(boxTop.X, boxTop.Y - size.Y - 2f * scale), text, color);
    }
}
