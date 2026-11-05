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

    /// <summary>Ids of the cards the user has folded (only their title shows). Saved with the configuration.</summary>
    public static HashSet<string> Collapsed { get; set; } = new();

    /// <summary>Called when the user folds or opens a card, so the plugin can save the list.</summary>
    public static Action? CollapsedChanged { get; set; }

    // The card being drawn (null outside a card) and whether its title has been drawn already.
    private static string? currentCard;
    private static bool currentTitleDrawn;

    // True while an empty clip rectangle hides what comes after the title of a folded card.
    private static bool clipPushed;

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
            DrawBox(id, estimate, content, true);

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
    private static void DrawBox(string id, float estimate, Action content, bool foldable = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X - RightMargin * scale;
        var height = MeasuredHeights.TryGetValue(id, out var known) ? known : estimate;
        var measured = 0f;

        // A folded card is only as tall as its title. Its content is still run but the box clips it, so nothing below the
        // title can be seen or clicked.
        var folded = foldable && Collapsed.Contains(id);
        var shownHeight = folded ? ImGui.GetTextLineHeight() + 2f * ImGui.GetStyle().WindowPadding.Y : height;

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        using (var child = ImRaii.Child(id, new Vector2(width, shownHeight), true, flags))
        {
            if (child.Success)
            {
                currentCard = foldable ? id : null;
                currentTitleDrawn = false;
                content();
                currentCard = null;

                if (clipPushed)
                {
                    ImGui.PopClipRect();
                    clipPushed = false;
                }

                // The cursor sits one item spacing below the last item; the box needs the padding below it instead.
                measured = MathF.Ceiling(ImGui.GetCursorPosY() - ItemSpacingY * scale + PaddingY * scale);
            }
        }

        // Remember it for the next frame (only real changes, so the height never jitters).
        if (!folded && measured > 0f && MathF.Abs(measured - height) >= 1f)
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
    /// <summary>Tooltip for the checkbox just drawn: what "Enabled" does for an element the game also draws.</summary>
    public static void EnabledHint(string? extra = null)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(extra is null ? "Draws NahjaUI's version and hides the game's." : $"Draws NahjaUI's version and hides the game's.\n{extra}");
    }

    public static void SectionLabel(string text)
    {
        // The first title of a card is its header: clicking it folds or opens the card.
        if (currentCard is null || currentTitleDrawn)
        {
            ImGui.TextColored(Theme.TextMuted, text.ToUpperInvariant());
            return;
        }

        currentTitleDrawn = true;
        var scale = ImGuiHelpers.GlobalScale;
        var folded = Collapsed.Contains(currentCard);

        var windowPos = ImGui.GetWindowPos();
        var windowWidth = ImGui.GetWindowWidth();
        var lineTop = ImGui.GetCursorScreenPos().Y;
        var lineHeight = ImGui.GetTextLineHeight();

        var rowMin = new Vector2(windowPos.X, lineTop - 4f * scale);
        var rowMax = new Vector2(windowPos.X + windowWidth, lineTop + lineHeight + 4f * scale);
        var hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, rowMax);

        ImGui.TextColored(hovered ? Theme.Text : Theme.TextMuted, text.ToUpperInvariant());

        // A small triangle at the right: down when the card is open, pointing left when it is folded.
        var drawList = ImGui.GetWindowDrawList();
        var size = 4f * scale;
        var center = new Vector2(windowPos.X + windowWidth - 18f * scale, lineTop + lineHeight / 2f);
        var color = ImGui.GetColorU32(hovered ? Theme.Text : Theme.TextMuted);
        if (folded)
            drawList.AddTriangleFilled(new Vector2(center.X + size, center.Y - size), new Vector2(center.X + size, center.Y + size), new Vector2(center.X - size, center.Y), color);
        else
            drawList.AddTriangleFilled(new Vector2(center.X - size, center.Y - size / 2f), new Vector2(center.X + size, center.Y - size / 2f), new Vector2(center.X, center.Y + size / 2f + size / 2f), color);

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (!Collapsed.Remove(currentCard))
                Collapsed.Add(currentCard);

            CollapsedChanged?.Invoke();
        }

        // Folded: from here on everything is clipped away with an empty rectangle, so no part of the first control peeks out
        // under the title and nothing can be hovered or clicked.
        if (folded)
        {
            ImGui.PushClipRect(Vector2.Zero, Vector2.Zero, true);
            clipPushed = true;
        }
    }
}
