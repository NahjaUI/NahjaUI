using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.EnemyList;

/// <summary>The "Enemy list" page of the main window.</summary>
internal sealed class EnemyListEditor
{
    private readonly Plugin plugin;

    public EnemyListEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var settings = plugin.Configuration.EnemyList;
        plugin.EnemyListSystem.PreviewRequested = true;

        ImGui.TextColored(Theme.TextMuted, "The enemies that are fighting you, like the game's own list (up to 8).");
        ImGui.TextColored(Theme.TextMuted, "Move it and change its width from Layout or unlock mode. Samples show while nobody fights you.");
        ImGui.Spacing();

        Widgets.Card("##card_el_general", 5, () =>
        {
            Widgets.SectionLabel("Enemy list");

            Check("Enabled", settings.Enabled, v => settings.Enabled = v);
            Check("Hide the game's enemy list", settings.HideNative, v => settings.HideNative = v);
            Check("Click on a bar to target (right click: menu)", settings.Clickable, v => settings.Clickable = v);
        });

        Widgets.Card("##card_el_bar", 8, () =>
        {
            Widgets.SectionLabel("Bars");

            var layout = plugin.EnemyListSystem.Element.Layout;
            var height = layout.Height;
            if (ImGui.InputInt("Height (px)", ref height, 2, 10))
            {
                layout.Height = Math.Clamp(height, 6, 100);
                plugin.RequestSave();
            }

            Slider("Corner rounding (px)", settings.RoundingPx, 0, 12, v => settings.RoundingPx = v);
            Slider("Space between bars (px)", settings.SpacingPx, 0, 30, v => settings.SpacingPx = v);
            Slider("Text size (px)", settings.TextSize, 8, 30, v => settings.TextSize = v);
            Check("Show the life percentage", settings.ShowPercent, v => settings.ShowPercent = v);
            Color("Life color", settings.LifeColor, v => settings.LifeColor = v);
            Color("Background color", settings.BackgroundColor, v => settings.BackgroundColor = v);
        });

        Widgets.Card("##card_el_dot", 4, () =>
        {
            Widgets.SectionLabel("Aggro dot");

            Check("Show the dot", settings.ShowAggroDot, v => settings.ShowAggroDot = v);
            Color("Enemy is attacking me", settings.OnMeColor, v => settings.OnMeColor = v);
            Color("Enemy is attacking someone else", settings.OnOthersColor, v => settings.OnOthersColor = v);
        });

        Widgets.Card("##card_el_cast", 5, () =>
        {
            Widgets.SectionLabel("Casts");

            Check("Show the cast under each bar", settings.ShowCast, v => settings.ShowCast = v);
            Slider("Cast height (px)", settings.CastHeightPx, 6, 30, v => settings.CastHeightPx = v);
            Slider("Space under the bar (px)", settings.CastGapPx, 0, 12, v => settings.CastGapPx = v);
            ImGui.TextColored(Theme.TextMuted, "Colors: the target cast bar (Cast bars page). A different color means it cannot be interrupted.");
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
