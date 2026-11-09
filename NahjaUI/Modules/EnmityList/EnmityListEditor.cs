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
            Color("Background color", settings.BackgroundColor, v => settings.BackgroundColor = v);
            Color("Border color", settings.BorderColor, v => settings.BorderColor = new Vector4(v.X, v.Y, v.Z, 1f));
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

        Widgets.Card("##card_el_dot", 8, () =>
        {
            Widgets.SectionLabel("Enmity dot");

            Check("Show the dot", settings.ShowEnmityDot, v => settings.ShowEnmityDot = v);
            Slider("Dot size (px)", settings.EnmityDotSizePx, 4, 30, v => settings.EnmityDotSizePx = v);
            var dotAnchor = settings.EnmityDotAnchor;
            if (HudBarEditor.EnumCombo("Dot position", ref dotAnchor, point => point.Label()))
            {
                settings.EnmityDotAnchor = dotAnchor;
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

            var side = settings.ProgressBarPosition;
            var sideLabel = side switch { ProgressBarSide.Right => "Right of the bar", ProgressBarSide.Left => "Left of the bar", _ => "Under the bar" };
            if (ImGui.BeginCombo("Progress bar position", sideLabel))
            {
                foreach (var option in new[] { ProgressBarSide.Below, ProgressBarSide.Right, ProgressBarSide.Left })
                {
                    var label = option switch { ProgressBarSide.Right => "Right of the bar", ProgressBarSide.Left => "Left of the bar", _ => "Under the bar" };
                    if (ImGui.Selectable(label, option == side))
                    {
                        settings.ProgressBarPosition = option;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            Slider("Progress bar width (px, 0 = same as the HP bar)", settings.ProgressBarWidthPx, 0, 600, v => settings.ProgressBarWidthPx = v);
            Slider("Progress bar height (px)", settings.ProgressBarHeightPx, 6, 30, v => settings.ProgressBarHeightPx = v);
            Slider("Space between HP bar and progress bar (px)", settings.ProgressBarGapPx, 0, 40, v => settings.ProgressBarGapPx = v);
            Offset("Progress bar offset X (px)", settings.ProgressBarOffsetX, v => settings.ProgressBarOffsetX = v);
            // Up is plus: the saved value counts downwards, so the sign is turned here.
            Offset("Progress bar offset Y (px)", -settings.ProgressBarOffsetY, v => settings.ProgressBarOffsetY = -v);
            Color("Color when it can be interrupted", settings.ProgressBarColor, v => settings.ProgressBarColor = new Vector4(v.X, v.Y, v.Z, 1f));
            Color("Color when it cannot be interrupted", settings.ProgressBarUninterruptibleColor, v => settings.ProgressBarUninterruptibleColor = new Vector4(v.X, v.Y, v.Z, 1f));
            Color("Progress bar background color", settings.ProgressBarBackgroundColor, v => settings.ProgressBarBackgroundColor = v);
            Color("Progress bar border color", settings.ProgressBarBorderColor, v => settings.ProgressBarBorderColor = new Vector4(v.X, v.Y, v.Z, 1f));
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
        plugin.HudBarEditorPage.DrawStatusRowCard("##card_el_enfeeblements", "Enfeeblements", settings.Enfeeblements, false, canDispel: false);
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
            apply(Math.Clamp(value, -300, 300));
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
