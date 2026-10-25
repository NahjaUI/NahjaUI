using System;
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

    /// <summary>
    /// A rounded panel with a fixed height of <paramref name="lines"/> text lines.
    /// If the content is taller than expected, the card scrolls instead of overflowing.
    /// </summary>
    public static void Card(string id, int lines, Action content)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - RightMargin * scale;
        var height = lines * ImGui.GetTextLineHeightWithSpacing() + 24f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.Card))
        using (var child = ImRaii.Child(id, new Vector2(width, height), true))
        {
            if (child.Success)
                content();
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// A highlighted message box. The colour (Theme.Info, Theme.Warning...) tints the background and the border.
    /// </summary>
    public static void Callout(string id, string text, Vector4 color, int lines = 2)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - RightMargin * scale;
        var height = lines * ImGui.GetTextLineHeightWithSpacing() + 20f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.WithAlpha(color, 0.16f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Theme.WithAlpha(color, 0.60f)))
        using (var child = ImRaii.Child(id, new Vector2(width, height), true))
        {
            if (child.Success)
                ImGui.TextWrapped(text);
        }

        ImGui.Spacing();
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
