using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.CastBars;

/// <summary>The "Cast bars" page of the main window: one card for the player's bar and one for the target's.</summary>
internal sealed class CastBarEditor
{
    private readonly Plugin plugin;

    public CastBarEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        // While this page is open, the bars show a sample cast so they can be adjusted without casting anything.
        plugin.CastBarSystem.PreviewRequested = true;

        var settings = plugin.Configuration.CastBars;

        DrawBar("Player cast bar", "##card_castbar_player", settings.Player, plugin.CastBarSystem.PlayerElement, false);
        DrawBar("Target cast bar", "##card_castbar_target", settings.Target, plugin.CastBarSystem.TargetElement, true);
    }

    private void DrawBar(string title, string cardId, CastBarSettings settings, LayoutElement element, bool isTarget)
    {
        Widgets.Card(cardId, isTarget ? 26 : 28, () =>
        {
            Widgets.SectionLabel(title);

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var hideNative = settings.HideNative;
            if (ImGui.Checkbox("Hide the game's cast bar", ref hideNative))
            {
                settings.HideNative = hideNative;
                plugin.RequestSave();
            }

            if (isTarget)
            {
                ImGui.TextColored(Theme.TextMuted, "This hides the game's separate target cast bar. If the game's own bar stays");
                ImGui.TextColored(Theme.TextMuted, "visible, set the target info to separate parts in the game's HUD layout.");
            }

            ImGui.Spacing();

            // The size belongs to the layout system, like the position.
            var layout = element.Layout;

            var width = layout.Width;
            if (ImGui.InputInt("Width (px)", ref width, 2, 10))
            {
                layout.Width = Math.Clamp(width, 1, 4000);
                plugin.RequestSave();
            }

            var height = layout.Height;
            if (ImGui.InputInt("Height (px)", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 1, 1000);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // The game's target cast bar has no icon, so there is no option for it.
            if (!isTarget)
            {
                var showIcon = settings.ShowIcon;
                if (ImGui.Checkbox("Show the spell icon inside the bar", ref showIcon))
                {
                    settings.ShowIcon = showIcon;
                    plugin.RequestSave();
                }
            }

            var showName = settings.ShowName;
            if (ImGui.Checkbox("Show the spell name", ref showName))
            {
                settings.ShowName = showName;
                plugin.RequestSave();
            }

            var showTime = settings.ShowTime;
            if (ImGui.Checkbox("Show the time", ref showTime))
            {
                settings.ShowTime = showTime;
                plugin.RequestSave();
            }

            var timeMode = settings.TimeMode;
            if (EnumCombo("Time format", ref timeMode, TimeModeLabel))
            {
                settings.TimeMode = timeMode;
                plugin.RequestSave();
            }

            // The slidecast marker only makes sense on your own bar.
            if (!isTarget)
            {
                var showSlide = settings.ShowSlideCast;
                if (ImGui.Checkbox("Show where slidecasting starts", ref showSlide))
                {
                    settings.ShowSlideCast = showSlide;
                    plugin.RequestSave();
                }

                var slideMs = settings.SlideCastMs;
                if (ImGui.SliderInt("Slidecast window (ms)", ref slideMs, 100, 1000))
                {
                    settings.SlideCastMs = slideMs;
                    plugin.RequestSave();
                }

                ImGui.TextColored(Theme.TextMuted, "500 ms is the game's fixed window, the same for every spell. A high ping");
                ImGui.TextColored(Theme.TextMuted, "gives a little extra that the game does not report: raise it only if you test it.");

                var slideColor = settings.SlideCastColor;
                if (ImGui.ColorEdit4("Slidecast color", ref slideColor, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.SlideCastColor = new Vector4(slideColor.X, slideColor.Y, slideColor.Z, 1f);
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            // Font sizes are fixed steps: each one is a real font, so the text is always sharp.
            if (ImGui.BeginCombo("Text size (px)", $"{settings.TextSize} px", ImGuiComboFlags.None))
            {
                foreach (var size in OverlayFonts.AvailableSizes)
                {
                    if (ImGui.Selectable($"{size} px", settings.TextSize == size))
                    {
                        settings.TextSize = size;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var font = settings.Font;
            if (EnumCombo("Text font", ref font, FontLabel))
            {
                settings.Font = font;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var barColor = settings.BarColor;
            if (ImGui.ColorEdit4("Bar color", ref barColor, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BarColor = new Vector4(barColor.X, barColor.Y, barColor.Z, 1f);
                plugin.RequestSave();
            }

            if (isTarget)
            {
                var uninterruptible = settings.UninterruptibleColor;
                if (ImGui.ColorEdit4("Color when it cannot be interrupted", ref uninterruptible, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.UninterruptibleColor = new Vector4(uninterruptible.X, uninterruptible.Y, uninterruptible.Z, 1f);
                    plugin.RequestSave();
                }
            }

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            var background = settings.BackgroundColor;
            if (ImGui.ColorEdit4("Background color", ref background, ImGuiColorEditFlags.None))
            {
                settings.BackgroundColor = background;
                plugin.RequestSave();
            }

            var border = settings.BorderColor;
            if (ImGui.ColorEdit4("Border color", ref border, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BorderColor = new Vector4(border.X, border.Y, border.Z, 1f);
                plugin.RequestSave();
            }
        });
    }

    private static string TimeModeLabel(CastTimeMode mode) => mode switch
    {
        CastTimeMode.Remaining => "Time left (1.4)",
        CastTimeMode.ElapsedOverTotal => "Done over total (0.6 / 2.0)",
        _ => mode.ToString()
    };

    private static string FontLabel(OverlayFont font) => font switch
    {
        OverlayFont.GameNumbers => "Game numbers (Meidinger)",
        OverlayFont.GameUi => "Game interface (Axis)",
        OverlayFont.GameUiBold => "Game interface, bold (Axis)",
        OverlayFont.Condensed => "Condensed (Trump Gothic)",
        OverlayFont.NahjaDisplay => "NahjaUI display (Saira Condensed)",
        OverlayFont.NahjaDisplayBold => "NahjaUI numbers (Saira Condensed, bold)",
        OverlayFont.NahjaText => "NahjaUI text (Saira)",
        _ => font.ToString()
    };

    // A drop-down list for any enum. Returns true when the user picked a different value.
    private static bool EnumCombo<T>(string label, ref T value, Func<T, string> labeler) where T : struct, Enum
    {
        var changed = false;

        if (ImGui.BeginCombo(label, labeler(value), ImGuiComboFlags.None))
        {
            foreach (var option in Enum.GetValues<T>())
            {
                if (ImGui.Selectable(labeler(option), EqualityComparer<T>.Default.Equals(option, value)))
                {
                    value = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }
}
