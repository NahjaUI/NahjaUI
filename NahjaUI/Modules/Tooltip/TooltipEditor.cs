using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.Tooltip;

/// <summary>The "Tooltip" page of the main window.</summary>
internal sealed class TooltipEditor
{
    private readonly Plugin plugin;

    public TooltipEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var s = plugin.Configuration.Tooltip;

        ImGui.TextColored(Theme.TextMuted, "Hover an icon on one of your frames to see how it looks.");
        ImGui.Spacing();

        Widgets.Card("##card_tt_general", 3, () =>
        {
            Widgets.SectionLabel("Tooltip");
            Check("Enabled", s.Enabled, v => s.Enabled = v);
        });

        Widgets.Card("##card_tt_where", 10, () =>
        {
            Widgets.SectionLabel("Where it appears");

            ImGui.TextColored(Theme.TextMuted, "Enhancements and enfeeblements of each frame. The target of target has none.");
            Check("Player: enhancements", s.PlayerEnhancements, v => s.PlayerEnhancements = v);
            Check("Player: enfeeblements", s.PlayerEnfeeblements, v => s.PlayerEnfeeblements = v);
            Check("Target: enhancements", s.TargetEnhancements, v => s.TargetEnhancements = v);
            Check("Target: enfeeblements", s.TargetEnfeeblements, v => s.TargetEnfeeblements = v);
            Check("Focus: enhancements", s.FocusEnhancements, v => s.FocusEnhancements = v);
            Check("Focus: enfeeblements", s.FocusEnfeeblements, v => s.FocusEnfeeblements = v);
            Check("Party: enhancements", s.PartyEnhancements, v => s.PartyEnhancements = v);
            Check("Party: enfeeblements", s.PartyEnfeeblements, v => s.PartyEnfeeblements = v);
        });

        Widgets.Card("##card_tt_content", 5, () =>
        {
            Widgets.SectionLabel("What it says");

            ImGui.TextColored(Theme.TextMuted, "The name and the description are always shown.");
            Check("Status ID", s.ShowId, v => s.ShowId = v);
            Check("Who applied it", s.ShowSource, v => s.ShowSource = v);
        });

        Widgets.Card("##card_tt_look", 13, () =>
        {
            Widgets.SectionLabel("Look");

            Slider("Text size (px)", s.TextSize, 10, 24, v => s.TextSize = v);
            Slider("Maximum width (px)", s.MaxWidthPx, 160, 600, v => s.MaxWidthPx = v);
            Slider("Corner rounding (px)", s.RoundingPx, 0, 12, v => s.RoundingPx = v);

            Check("The name uses the accent color", s.NameUsesAccent, v => s.NameUsesAccent = v);
            if (!s.NameUsesAccent)
                Color("Name color", s.NameColor, false, v => s.NameColor = v);

            Color("Description color", s.DescriptionColor, false, v => s.DescriptionColor = v);
            Color("Details color (ID, who applied it)", s.DetailColor, false, v => s.DetailColor = v);
            Color("Background color", s.BackgroundColor, true, v => s.BackgroundColor = v);
            Color("Border color", s.BorderColor, false, v => s.BorderColor = v);

            if (ImGui.Button("Reset the look"))
            {
                var fresh = new TooltipSettings();
                s.TextSize = fresh.TextSize;
                s.MaxWidthPx = fresh.MaxWidthPx;
                s.RoundingPx = fresh.RoundingPx;
                s.NameUsesAccent = fresh.NameUsesAccent;
                s.NameColor = fresh.NameColor;
                s.DescriptionColor = fresh.DescriptionColor;
                s.DetailColor = fresh.DetailColor;
                s.BackgroundColor = fresh.BackgroundColor;
                s.BorderColor = fresh.BorderColor;
                plugin.RequestSave();
            }
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

    private void Color(string label, Vector4 current, bool alpha, Action<Vector4> apply)
    {
        var value = current;
        if (ImGui.ColorEdit4(label, ref value, alpha ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoAlpha))
        {
            apply(alpha ? value : new Vector4(value.X, value.Y, value.Z, 1f));
            plugin.RequestSave();
        }
    }
}
