using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Modules.UnitFrames;

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
            Check("Show the game's values next to each bar (test)", settings.ShowGameValues, v => settings.ShowGameValues = v);
            Check("Keep the game's list visible too (test)", settings.ShowGameList, v => settings.ShowGameList = v);
        });

        Widgets.Card("##card_el_bar", 8, () =>
        {
            Widgets.SectionLabel("Bars");

            var layout = plugin.EnmityListSystem.Element.Layout;

            var width = layout.Width;
            if (ImGui.InputInt("Width (px)", ref width, 2, 10))
            {
                layout.Width = Math.Clamp(width, 40, 1000);
                plugin.RequestSave();
            }

            var height = layout.Height;
            if (ImGui.InputInt("Height (px)", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 6, 100);
                plugin.RequestSave();
            }

            Slider("Corner rounding (px)", settings.RoundingPx, 0, 12, v => settings.RoundingPx = v);
            Slider("Space between bars (px)", settings.SpacingPx, 0, 30, v => settings.SpacingPx = v);
            Color("HP color", settings.HpColor, v => settings.HpColor = v);
            Color("Background color", settings.BackgroundColor, v => settings.BackgroundColor = v);
        });

        Widgets.Card("##card_el_name", 12, () =>
        {
            Widgets.SectionLabel("Name");

            Check("Show the name", settings.Name.Enabled, v => settings.Name.Enabled = v);
            ImGui.TextColored(Theme.TextMuted, "Enemy names are written as the game gives them (the game has no option for it).");

            if (UnitFrameEditor.TextControls(settings.Name))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_hp", 11, () =>
        {
            Widgets.SectionLabel("HP percentage");

            Check("Show the HP percentage", settings.Health.Enabled, v => settings.Health.Enabled = v);

            if (UnitFrameEditor.TextControls(settings.Health))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_dot", 8, () =>
        {
            Widgets.SectionLabel("Enmity dot");

            Check("Show the dot", settings.ShowEnmityDot, v => settings.ShowEnmityDot = v);
            Slider("Dot size (px)", settings.EnmityDotSizePx, 4, 30, v => settings.EnmityDotSizePx = v);
            var dotAnchor = settings.EnmityDotAnchor;
            if (UnitFrameEditor.EnumCombo("Dot position", ref dotAnchor, point => point.Label()))
            {
                settings.EnmityDotAnchor = dotAnchor;
                plugin.RequestSave();
            }

            Slider("Dot offset X (px)", settings.EnmityDotOffsetX, -300, 300, v => settings.EnmityDotOffsetX = v);
            Slider("Dot offset Y (px)", -settings.EnmityDotOffsetY, -300, 300, v => settings.EnmityDotOffsetY = -v);
            Color("Enemy is attacking me", settings.OnMeColor, v => settings.OnMeColor = v);
            Color("Enemy is attacking someone else", settings.OnOthersColor, v => settings.OnOthersColor = v);
        });

        Widgets.Card("##card_el_cast", 10, () =>
        {
            Widgets.SectionLabel("Casts");

            ImGui.TextColored(Theme.TextMuted, "Whether the casts and the list itself show follow the game's own enmity list options.");
            ImGui.Spacing();

            var side = settings.CastSide;
            var sideLabel = side switch { EnemyCastSide.Right => "Right of the bar", EnemyCastSide.Left => "Left of the bar", _ => "Under the bar" };
            if (ImGui.BeginCombo("Cast position", sideLabel))
            {
                foreach (var option in new[] { EnemyCastSide.Below, EnemyCastSide.Right, EnemyCastSide.Left })
                {
                    var label = option switch { EnemyCastSide.Right => "Right of the bar", EnemyCastSide.Left => "Left of the bar", _ => "Under the bar" };
                    if (ImGui.Selectable(label, option == side))
                    {
                        settings.CastSide = option;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            Slider("Cast width (px, 0 = same as the bar)", settings.CastWidthPx, 0, 600, v => settings.CastWidthPx = v);
            Slider("Cast height (px)", settings.CastHeightPx, 6, 30, v => settings.CastHeightPx = v);
            Slider("Space between bar and cast (px)", settings.CastGapPx, 0, 40, v => settings.CastGapPx = v);
            Slider("Cast offset X (px)", settings.CastOffsetX, -300, 300, v => settings.CastOffsetX = v);
            // Up is plus: the saved value counts downwards, so the sign is turned here.
            Slider("Cast offset Y (px)", -settings.CastOffsetY, -300, 300, v => settings.CastOffsetY = -v);
            ImGui.TextColored(Theme.TextMuted, "Colors: the target cast bar (Cast bars page). A different color means it cannot be interrupted.");
        });

        Widgets.Card("##card_el_cast_name", 12, () =>
        {
            Widgets.SectionLabel("Cast: spell name");

            Check("Show the spell name", settings.CastName.Enabled, v => settings.CastName.Enabled = v);

            if (UnitFrameEditor.TextControls(settings.CastName))
                plugin.RequestSave();
        });

        Widgets.Card("##card_el_cast_time", 12, () =>
        {
            Widgets.SectionLabel("Cast: time left");

            Check("Show the time left", settings.CastTime.Enabled, v => settings.CastTime.Enabled = v);

            if (UnitFrameEditor.TextControls(settings.CastTime))
                plugin.RequestSave();
        });
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
