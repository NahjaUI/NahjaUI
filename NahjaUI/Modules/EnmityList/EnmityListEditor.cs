using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.EnmityList;

/// <summary>The "Enmity list" page of the main window.</summary>
internal sealed class EnmityListEditor
{
    private readonly Plugin plugin;

    public EnmityListEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.EnmityList;
        plugin.EnmityListSystem.PreviewRequested = true;

        Widgets.Card("##card_el_general", 5, () =>
        {
            Widgets.SectionLabel("Enmity list");

            Check("Enabled", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
        });

        Widgets.Card("##card_el_bar", 8, () =>
        {
            Widgets.SectionLabel("Bars");

            var layout = plugin.EnmityListSystem.Element.Layout;

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

            Slider("Space between bars (px)", settings.SpacingPx, 0, 30, v => settings.SpacingPx = v);
            Color("HP color", settings.HpColor, v => settings.HpColor = v);
            if (HudBarEditor.BackgroundAndBorder("bgbd13", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_dot", 9, () =>
        {
            Widgets.SectionLabel("Enmity dot");

            Check("Show the dot", settings.ShowEnmityDot, v => settings.ShowEnmityDot = v);
            Slider("Dot size (px)", settings.EnmityDotSizePx, 4, 30, v => settings.EnmityDotSizePx = v);
            var dotAnchor = settings.EnmityDotAnchor;
            if (HudBarEditor.EnumCombo("Dot anchor on the bar", ref dotAnchor, point => point.Label()))
            {
                settings.EnmityDotAnchor = dotAnchor;
                plugin.RequestSave();
            }

            var dotSelfAnchor = settings.EnmityDotSelfAnchor;
            if (HudBarEditor.EnumCombo("Dot anchor of the dot", ref dotSelfAnchor, point => point.Label()))
            {
                settings.EnmityDotSelfAnchor = dotSelfAnchor;
                plugin.RequestSave();
            }

            Offset("Dot offset X (px)", settings.EnmityDotOffsetX, v => settings.EnmityDotOffsetX = v);
            Offset("Dot offset Y (px)", -settings.EnmityDotOffsetY, v => settings.EnmityDotOffsetY = -v);
            Color("Enemy is attacking me", settings.OnMeColor, v => settings.OnMeColor = v);
            Color("Enemy is not attacking me", settings.OnOthersColor, v => settings.OnOthersColor = v);
        });

        Widgets.Card("##card_el_progress_bar", 14, () =>
        {
            Widgets.SectionLabel("Progress bar");

            ImGui.TextColored(Theme.TextMuted, "Whether the progress bars and the list itself show follow the game's own enmity list options.");
            ImGui.Spacing();

            var barAnchor = settings.ProgressBarBarAnchor;
            if (HudBarEditor.EnumCombo("Progress bar anchor on the bar", ref barAnchor, point => point.Label()))
            {
                settings.ProgressBarBarAnchor = barAnchor;
                plugin.RequestSave();
            }

            var selfAnchor = settings.ProgressBarSelfAnchor;
            if (HudBarEditor.EnumCombo("Progress bar anchor of the progress bar", ref selfAnchor, point => point.Label()))
            {
                settings.ProgressBarSelfAnchor = selfAnchor;
                plugin.RequestSave();
            }

            Size("Progress bar width (px, 0 = same as the HP bar)", settings.ProgressBarWidthPx, 0, 4000, v => settings.ProgressBarWidthPx = v);
            Size("Progress bar height (px)", settings.ProgressBarHeightPx, 1, 1000, v => settings.ProgressBarHeightPx = v);
            Offset("Progress bar offset X (px)", settings.ProgressBarOffsetX, v => settings.ProgressBarOffsetX = v);
            // Up is plus: the saved value counts downwards, so the sign is turned here.
            Offset("Progress bar offset Y (px)", -settings.ProgressBarOffsetY, v => settings.ProgressBarOffsetY = -v);
            Color("Color when it can be interrupted", settings.ProgressBarColor, v => settings.ProgressBarColor = new Vector4(v.X, v.Y, v.Z, 1f));
            Color("Color when it cannot be interrupted", settings.ProgressBarUninterruptibleColor, v => settings.ProgressBarUninterruptibleColor = new Vector4(v.X, v.Y, v.Z, 1f));
            if (HudBarEditor.BackgroundAndBorder("bgbd14", "Progress bar background color", "Progress bar border color",
                settings.CustomProgressBarBackground, settings.ProgressBarBackgroundColor, onBg => settings.CustomProgressBarBackground = onBg, colBg => settings.ProgressBarBackgroundColor = colBg,
                settings.CustomProgressBarBorder, settings.ProgressBarBorderColor, onBd => settings.CustomProgressBarBorder = onBd, colBd => settings.ProgressBarBorderColor = colBd))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_name", 12, () =>
        {
            Widgets.SectionLabel("Name");

            Check("Show the name", settings.Name.Enabled, v => settings.Name.Enabled = v);
            ImGui.TextColored(Theme.TextMuted, "Enemy names are written as the game gives them (the game has no option for it).");

            if (HudBarEditor.TextControls(settings.Name))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_hp", 11, () =>
        {
            Widgets.SectionLabel("HP percentage");

            Check("Show the HP percentage", settings.Hp.Enabled, v => settings.Hp.Enabled = v);

            if (HudBarEditor.TextControls(settings.Hp))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_progress_bar_name", 12, () =>
        {
            Widgets.SectionLabel("Progress bar: action name");

            Check("Show the action name", settings.ProgressBarName.Enabled, v => settings.ProgressBarName.Enabled = v);

            if (HudBarEditor.TextControls(settings.ProgressBarName))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_progress_bar_time", 12, () =>
        {
            Widgets.SectionLabel("Progress bar: time left");

            Check("Show the time left", settings.ProgressBarTime.Enabled, v => settings.ProgressBarTime.Enabled = v);

            var timeMode = settings.ProgressBarTimeMode;
            if (HudBarEditor.EnumCombo("Time format", ref timeMode, NahjaUI.Modules.ProgressBars.ProgressBarEditor.TimeModeLabel))
            {
                settings.ProgressBarTimeMode = timeMode;
                plugin.RequestSave();
            }

            if (HudBarEditor.TextControls(settings.ProgressBarTime))
                plugin.RequestSave();
        });

        plugin.HudBarEditorPage.DrawStatusRowCard("##card_el_enhancements", "Enhancements", settings.Enhancements, true);
        plugin.HudBarEditorPage.DrawStatusRowCard("##card_el_enfeeblements", "Enfeeblements", settings.Enfeeblements, false);
    }

    private void Check(string label, bool current, Action<bool> apply)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            apply(value);
            plugin.RequestSave();
        }
    }

    // An offset: a numeric field with the small +/- buttons (1 px, 5 with Ctrl), like every other offset of the interface.
    private void Offset(string label, int current, Action<int> apply)
    {
        var value = current;
        if (ImGui.InputInt(label, ref value, 1, 5))
        {
            apply(Math.Clamp(value, -500, 500));
            plugin.RequestSave();
        }
    }

    // A size: a numeric field with the small +/- buttons (2 px, 10 with Ctrl), like every other width and height of the interface.
    private void Size(string label, int current, int min, int max, Action<int> apply)
    {
        var value = current;
        if (ImGui.InputInt(label, ref value, 2, 10))
        {
            apply(Math.Clamp(value, min, max));
            plugin.RequestSave();
        }
    }

    private void Slider(string label, int current, int min, int max, Action<int> apply)
    {
        var value = current;
        if (ImGui.SliderInt(label, ref value, min, max))
        {
            apply(value);
            plugin.RequestSave();
        }
    }

    private void Color(string label, Vector4 current, Action<Vector4> apply)
    {
        var value = current;
        if (ImGui.ColorEdit4(label, ref value, ImGuiColorEditFlags.None))
        {
            apply(value);
            plugin.RequestSave();
        }
    }
}
