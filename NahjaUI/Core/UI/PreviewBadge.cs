using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The mark that tells that what is on screen is a preview of the settings page (made-up data), so it is never taken for real.
/// </summary>
internal static class PreviewBadge
{
    /// <summary>Draws "Preview" just above <paramref name="boxTop"/> (the top left corner of the element).</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 boxTop)
    {
        const string text = "PREVIEW";
        var scale = UiScale.Overlay;
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(boxTop.X, boxTop.Y - size.Y - 2f * scale);

        OutlinedText.Draw(drawList, pos, text, ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.25f, 0.9f)));
    }
}
