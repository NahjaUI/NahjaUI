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
    /// The body of a tab, in a space of its own that fills what is left under the tab bar. It scrolls by itself, so the tabs above never
    /// scroll away. Use it as <c>using (Widgets.TabBody(id)) { ... }</c> inside the tab, and close the tab after it. When the body holds
    /// nothing but another tab bar, pass <paramref name="scroll"/> false: the tabs inside scroll their own bodies.
    /// </summary>
    public static ImRaii.ChildDisposable TabBody(string id, bool scroll = true)
    {
        var flags = scroll ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        return ImRaii.Child(id, Vector2.Zero, false, flags);
    }

    /// <summary>
    /// A highlighted message box. The colour (Theme.Info, Theme.Warning...) tints the background and the border.
    /// Its height follows the text, like a card.
    /// </summary>
    public static void Callout(string id, string text, Vector4 color, int lines = 2, Action? extra = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var estimate = lines * ImGui.GetTextLineHeightWithSpacing() + 20f * scale;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Theme.WithAlpha(color, 0.16f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Theme.WithAlpha(color, 0.60f)))
            DrawBox(id, estimate, () =>
            {
                ImGui.TextWrapped(text);
                extra?.Invoke();
            });

        ImGui.Spacing();
    }

    // The cards that already failed once (the error is written to the log only the first time).
    private static readonly System.Collections.Generic.HashSet<string> FailedCards = new();

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

                // A card that fails shows a short message instead of taking the whole page (and the game's frame) down.
                try
                {
                    content();
                }
                catch (Exception ex)
                {
                    if (FailedCards.Add(id))
                        Plugin.Log.Error(ex, "NahjaUI: the card {Id} failed.", id);

                    ImGui.TextColored(Theme.Error, "This card could not be drawn.");
                }

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

    // A fresh settings object of each kind, with the values the code gives by default (what "Reset" of a color goes back to).
    private static readonly Dictionary<Type, object> DefaultInstances = new();

    /// <summary>
    /// The default values of a kind of settings: a fresh object of the same type as <paramref name="like"/>.
    /// For the original color of a "Reset" button: <c>Widgets.Default(settings).BarColor</c>.
    /// </summary>
    public static T Default<T>(T like) where T : class
    {
        // The real type of the object, not the type of the variable: a gauge held as its base class must give ITS defaults.
        var type = like.GetType();
        if (!DefaultInstances.TryGetValue(type, out var instance))
        {
            instance = Activator.CreateInstance(type, true)!;
            DefaultInstances[type] = instance;
        }

        return (T)instance;
    }

    /// <summary>
    /// A color setting: a swatch with its name. Clicking it opens the picker, with only the hexadecimal value (to copy one from somewhere)
    /// and, when the original color is known, a "Reset" button that brings it back. Returns true when the color changed.
    /// </summary>
    public static bool ColorSetting(string label, ref Vector4 value, Vector4? original, bool alpha = false)
    {
        var changed = false;
        var flags = (alpha ? ImGuiColorEditFlags.AlphaBar : ImGuiColorEditFlags.NoAlpha) | ImGuiColorEditFlags.DisplayHex;
        var popupId = "##picker_" + label;
        var side = ImGui.GetFrameHeight();

        // NoTooltip: the game's tooltip lists RGB and other numbers; here only the hexadecimal value is shown.
        var swatchFlags = (alpha ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoAlpha) | ImGuiColorEditFlags.NoTooltip;
        if (ImGui.ColorButton("##swatch_" + label, value, swatchFlags, new Vector2(side, side)))
            ImGui.OpenPopup(popupId);

        if (ImGui.IsItemHovered())
        {
            static int Byte(float channel) => (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
            var hex = $"#{Byte(value.X):X2}{Byte(value.Y):X2}{Byte(value.Z):X2}";
            if (alpha)
                hex += $"{Byte(value.W):X2}";

            ImGui.SetTooltip(hex);
        }

        // What comes after "##" is only an id, not part of the name.
        var hidden = label.IndexOf("##", StringComparison.Ordinal);
        ImGui.SameLine();
        ImGui.TextUnformatted(hidden >= 0 ? label.Substring(0, hidden) : label);

        if (ImGui.BeginPopup(popupId))
        {
            if (ImGui.ColorPicker4("##picker", ref value, flags))
            {
                if (!alpha)
                    value = new Vector4(value.X, value.Y, value.Z, 1f);

                changed = true;
            }

            if (original is { } reset && ImGui.Button("Reset"))
            {
                value = reset;
                changed = true;
            }

            ImGui.EndPopup();
        }

        return changed;
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

    /// <summary>Tooltip for the checkbox just drawn: what "Enabled" does for an element the game also draws.</summary>
    public static void EnabledHint(string? extra = null)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(extra is null ? "Draws NahjaUI's version and hides the game's." : $"Draws NahjaUI's version and hides the game's.\n{extra}");
    }

    /// <summary>Tooltip for the "Enabled" checkbox of an element that has no game version to hide.</summary>
    public static void EnabledHintOnly(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    /// <summary>Small muted title in capitals that opens a group of settings.</summary>
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
