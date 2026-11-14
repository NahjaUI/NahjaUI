using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.ProgressBars;

/// <summary>The "Progress bars" page of the main window: one card for the player's bar and one for the target's.</summary>
internal sealed class ProgressBarEditor
{
    private readonly Plugin plugin;

    public ProgressBarEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        // While this page is open, the bars show a sample cast so they can be adjusted without casting anything.
        plugin.ProgressBarSystem.PreviewRequested = true;

        var settings = plugin.Configuration.ProgressBars;

        if (!ImGui.BeginTabBar("##pb_tabs"))
            return;

        if (ImGui.BeginTabItem("Player Progress Bar"))
        {
            using (Widgets.TabBody("##pb_body_player"))
                DrawBar("Player progress bar", "##card_progressbar_player", settings.Player, plugin.ProgressBarSystem.PlayerElement, false);

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Target Progress Bar"))
        {
            using (Widgets.TabBody("##pb_body_target"))
                DrawBar("Target progress bar", "##card_progressbar_target", settings.Target, plugin.ProgressBarSystem.TargetElement, true);

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Focus Progress Bar"))
        {
            using (Widgets.TabBody("##pb_body_focus"))
                DrawBar("Focus progress bar", "##card_progressbar_focus", settings.Focus, plugin.ProgressBarSystem.FocusElement, true, true);

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Global Cooldown Bar"))
        {
            using (Widgets.TabBody("##pb_body_gcd"))
                DrawGcd(settings.Gcd, plugin.ProgressBarSystem.GcdElement);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGcd(GcdBarSettings settings, LayoutElement element)
    {
        Widgets.Card("##card_progressbar_gcd", 24, () =>
        {
            Widgets.SectionLabel("Global cooldown bar");

            bool Check(string label, bool value, Action<bool> set)
            {
                if (!ImGui.Checkbox(label, ref value))
                    return false;
                set(value);
                plugin.RequestSave();
                return true;
            }

            Check("Enabled", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHintOnly("The game has no global cooldown bar, so there is nothing to hide: this one is only added.");
            ImGui.TextColored(Theme.TextMuted, "A thin bar right above your progress bar. It fills while the global cooldown runs.");
            Check("Always visible (empty when the global cooldown is not running)", settings.AlwaysVisible, v => settings.AlwaysVisible = v);

            ImGui.Spacing();

            var layout = element.Layout;
            var width = layout.Width;
            if (ImGui.InputInt("Width (px)##gcd", ref width, 2, 10))
            {
                layout.Width = Math.Clamp(width, 1, 4000);
                plugin.RequestSave();
            }

            var height = layout.Height;
            if (ImGui.InputInt("Height (px)##gcd", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 1, 100);
                plugin.RequestSave();
            }

            ImGui.Spacing();

            Check("Show the queue window", settings.ShowQueueWindow, v => settings.ShowQueueWindow = v);
            var queue = settings.QueueMs;
            if (ImGui.SliderInt("Queue window (ms)", ref queue, 100, 1000))
            {
                settings.QueueMs = queue;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "The last part of the cooldown: an action pressed here waits and is used the moment it ends.");

            Check("Show the weave limit", settings.ShowWeaveWindow, v => settings.ShowWeaveWindow = v);
            var weave = settings.WeaveMs;
            if (ImGui.SliderInt("Weave limit (ms before the end)", ref weave, 100, 1200))
            {
                settings.WeaveMs = weave;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "An ability used after this line delays your next global action. About 700 ms: tell me if it fits.");

            ImGui.Spacing();

            ColorRow("Bar color##gcd", settings.BarColor, c => settings.BarColor = c, true, Widgets.Default(settings).BarColor);
            ColorRow("Queue window color", settings.QueueColor, c => settings.QueueColor = c, true, Widgets.Default(settings).QueueColor);
            ColorRow("Weave limit color", settings.WeaveColor, c => settings.WeaveColor = c, true, Widgets.Default(settings).WeaveColor);
            if (HudBarEditor.BackgroundAndBorder("bgbd12", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
        });
    }

    private void ColorRow(string label, Vector4 value, Action<Vector4> set, bool noAlpha, Vector4 original)
    {
        if (Widgets.ColorSetting(label, ref value, original, !noAlpha))
        {
            set(value);
            plugin.RequestSave();
        }
    }

    private void DrawBar(string title, string cardId, ProgressBarSettings settings, LayoutElement element, bool isTarget, bool isFocus = false)
    {
        // What "Reset" of a color goes back to: the player's, the target's and the focus's bars start with different colors.
        var defaults = Widgets.Default(plugin.Configuration.ProgressBars);
        var original = isFocus ? defaults.Focus : isTarget ? defaults.Target : defaults.Player;


        Widgets.Card(cardId, isTarget ? 20 : 22, () =>
        {
            Widgets.SectionLabel(title);

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }
            Widgets.EnabledHint(isFocus ? "The game's own focus cast bar is part of its focus frame: it is hidden when the Focus frame of NahjaUI is enabled." : isTarget ? "The target progress bar is only hidden if the game's progress bar is separate from the target frame: set the target info to separate parts in the game's HUD layout." : null);

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

            // The game's target progress bar has no icon, so there is no option for it.
            if (!isTarget)
            {
                var showIcon = settings.ShowIcon;
                if (ImGui.Checkbox("Show the action icon", ref showIcon))
                {
                    settings.ShowIcon = showIcon;
                    plugin.RequestSave();
                }
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

                ImGui.TextColored(Theme.TextMuted, "500 ms is the game's fixed window, the same for every action. A high ping");
                ImGui.TextColored(Theme.TextMuted, "gives a little extra that the game does not report: raise it only if you test it.");

                var slideColor = settings.SlideCastColor;
                if (Widgets.ColorSetting("Slidecast color", ref slideColor, original.SlideCastColor))
                {
                    settings.SlideCastColor = slideColor;
                    plugin.RequestSave();
                }
            }

            ImGui.Spacing();

            var barColor = settings.BarColor;
            if (Widgets.ColorSetting(isTarget ? "Color when it can be interrupted" : "Bar color", ref barColor, original.BarColor))
            {
                settings.BarColor = barColor;
                plugin.RequestSave();
            }

            if (isTarget)
            {
                var uninterruptible = settings.UninterruptibleColor;
                if (Widgets.ColorSetting("Color when it cannot be interrupted", ref uninterruptible, original.UninterruptibleColor))
                {
                    settings.UninterruptibleColor = uninterruptible;
                    plugin.RequestSave();
                }
            }

            if (HudBarEditor.BackgroundAndBorder("bgbd11", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
        });

        // The action name and the time left: one card each, with the same controls as every other text.
        Widgets.Card(cardId + "_name", 12, () =>
        {
            Widgets.SectionLabel($"{title}: action name");

            var showName = settings.Name.Enabled;
            if (ImGui.Checkbox("Show the action name", ref showName))
            {
                settings.Name.Enabled = showName;
                plugin.RequestSave();
            }

            if (HudBarEditor.TextControls(settings.Name))
                plugin.RequestSave();
        });

        Widgets.Card(cardId + "_time", 13, () =>
        {
            Widgets.SectionLabel($"{title}: time left");

            var showTime = settings.Time.Enabled;
            if (ImGui.Checkbox("Show the time left", ref showTime))
            {
                settings.Time.Enabled = showTime;
                plugin.RequestSave();
            }

            var timeMode = settings.TimeMode;
            if (EnumCombo("Time format", ref timeMode, TimeModeLabel))
            {
                settings.TimeMode = timeMode;
                plugin.RequestSave();
            }

            if (HudBarEditor.TextControls(settings.Time))
                plugin.RequestSave();
        });
    }

    internal static string TimeModeLabel(ProgressBarTimeMode mode) => mode switch
    {
        ProgressBarTimeMode.Remaining => "Time left (1.4)",
        ProgressBarTimeMode.ElapsedOverTotal => "Done over total (0.6 / 2.0)",
        _ => mode.ToString()
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
