using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace NahjaUI.Core.Ui;

/// <summary>
/// Small reusable components shared by every page and module.
/// More arrive in later milestones (setting row, accordion, pill, tabs).
/// </summary>
internal static class Widgets
{
    private const float RightMargin = 16f;

    // Height each box needed the last time it was drawn (logical pixels times the UI scale, so physical), by id.
    private static readonly Dictionary<string, float> MeasuredHeights = new();

    // Space the theme leaves around the content of a box (see ThemeScope: WindowPadding and ItemSpacing).
    private const float PaddingY = 12f;
    private const float ItemSpacingY = 8f;

    /// <summary>
    /// A rounded panel that takes exactly the height of its content, so it never needs a scroll bar of its own and the
    /// mouse wheel always scrolls the page. The height is measured while drawing and used from the next frame on;
    /// the very first frame uses <paramref name="lines"/> text lines as a guess.
    /// </summary>
    public static void Card(string id, int lines, Action content)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var estimate = lines * ImGui.GetTextLineHeightWithSpacing() + 24f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Card))
            DrawBox(id, estimate, content);

        ImGui.Spacing();
    }

    /// <summary>
    /// A highlighted message box. The colour (Theme.Info, Theme.Warning...) tints the background and the border.
    /// Its height follows the text, like a card.
    /// </summary>
    public static void Callout(string id, string text, Vector4 color, int lines = 2)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var estimate = lines * ImGui.GetTextLineHeightWithSpacing() + 20f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.WithAlpha(color, 0.16f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Theme.WithAlpha(color, 0.60f)))
            DrawBox(id, estimate, () => ImGui.TextWrapped(text));

        ImGui.Spacing();
    }

    // The shared box: a bordered child window with no scrolling, sized from the last measurement.
    private static void DrawBox(string id, float estimate, Action content)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - RightMargin * scale;
        var height = MeasuredHeights.TryGetValue(id, out var known) ? known : estimate;
        var measured = 0f;

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        using (var child = ImRaii.Child(id, new Vector2(width, height), true, flags))
        {
            if (child.Success)
            {
                content();

                // The cursor sits one item spacing below the last item; the box needs the padding below it instead.
                measured = MathF.Ceiling(ImGui.GetCursorPosY() - ItemSpacingY * scale + PaddingY * scale);
            }
        }

        // Remember it for the next frame (only real changes, so the height never jitters).
        if (measured > 0f && MathF.Abs(measured - height) >= 1f)
            MeasuredHeights[id] = measured;
    }

    /// <summary>The main action of a page: filled with the accent colour.</summary>
    public static bool PrimaryButton(string label)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Theme.Accent))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.Mix(Theme.Accent, Vector4.One, 0.18f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, Theme.Mix(Theme.Accent, Vector4.One, 0.32f)))
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.TextOnAccent))
        {
            return ImGui.Button(label);
        }
    }

    /// <summary>Small muted title in capitals that opens a group of settings.</summary>
    public static void SectionLabel(string text)
    {
        ImGui.TextColored(Theme.TextMuted, text.ToUpperInvariant());
    }
}
