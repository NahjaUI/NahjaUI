using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The two buttons of the title bar (the big window and the small bar share them): an arrow that minimizes or restores,
/// and a cross that closes. Both are drawn with shapes (no font), centred exactly in the round highlight that appears
/// under the mouse.
/// </summary>
internal static class TitleButtons
{
    public enum Glyph
    {
        /// <summary>A small triangle pointing up, the same shape as the arrow of the cards. Always the same icon.</summary>
        Arrow,
        Close
    }

    /// <summary>The side of a button, in physical pixels (an even number, so the symbol is centred on a pixel).</summary>
    public static float Size => MathF.Round(24f * ImGuiHelpers.GlobalScale / 2f) * 2f;

    public static bool Draw(string id, Vector2 pos, Glyph glyph, string tooltip)
    {
        var size = Size;

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var center = pos + new Vector2(size / 2f, size / 2f);

        if (hovered)
            drawList.AddCircleFilled(center, size / 2f, ImGui.GetColorU32(Theme.WithAlpha(Theme.Accent, 0.30f)), 32);

        var color = ImGui.GetColorU32(hovered ? Theme.Text : Theme.TextMuted);

        if (glyph == Glyph.Arrow)
        {
            // The triangle is centred by its bounding box, so it sits in the middle of the circle.
            var half = size * 0.2f;
            drawList.AddTriangleFilled(
                new Vector2(center.X - half, center.Y + half * 0.75f),
                new Vector2(center.X + half, center.Y + half * 0.75f),
                new Vector2(center.X, center.Y - half * 0.75f),
                color);
        }
        else
        {
            var d = size * 0.2f;
            var thickness = MathF.Max(1f, MathF.Round(ImGuiHelpers.GlobalScale * 1.5f));
            drawList.AddLine(center + new Vector2(-d, -d), center + new Vector2(d, d), color, thickness);
            drawList.AddLine(center + new Vector2(d, -d), center + new Vector2(-d, d), color, thickness);
        }

        if (hovered)
            ImGui.SetTooltip(tooltip);

        return clicked;
    }
}
