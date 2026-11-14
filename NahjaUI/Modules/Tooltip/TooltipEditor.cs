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
            Widgets.EnabledHintOnly("This tooltip is only NahjaUI's, for its own icons: there is no game window to hide.");
        });

        Widgets.Card("##card_tt_where", 12, () =>
        {
            Widgets.SectionLabel("Where it appears");

            ImGui.TextColored(Theme.TextMuted, "Enhancements and enfeeblements of each frame and nameplate. The target of target has none.");
            Check("Player: enhancements", s.PlayerEnhancements, v => s.PlayerEnhancements = v);
            Check("Player: enfeeblements", s.PlayerEnfeeblements, v => s.PlayerEnfeeblements = v);
            Check("Target: enhancements", s.TargetEnhancements, v => s.TargetEnhancements = v);
            Check("Target: enfeeblements", s.TargetEnfeeblements, v => s.TargetEnfeeblements = v);
            Check("Focus: enhancements", s.FocusEnhancements, v => s.FocusEnhancements = v);
            Check("Focus: enfeeblements", s.FocusEnfeeblements, v => s.FocusEnfeeblements = v);
            Check("Party: enhancements", s.PartyEnhancements, v => s.PartyEnhancements = v);
            Check("Party: enfeeblements", s.PartyEnfeeblements, v => s.PartyEnfeeblements = v);
            Check("Nameplates: enhancements", s.NameplateEnhancements, v => s.NameplateEnhancements = v);
            Check("Nameplates: enfeeblements", s.NameplateEnfeeblements, v => s.NameplateEnfeeblements = v);
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

            TextSizeCombo(s);
            Slider("Maximum width (px)", s.MaxWidthPx, 160, 600, v => s.MaxWidthPx = v);
            Check("The name uses the accent color", s.NameUsesAccent, v => s.NameUsesAccent = v);
            if (!s.NameUsesAccent)
                Color("Name color", s.NameColor, false, v => s.NameColor = v, Widgets.Default(s).NameColor);

            Color("Description color", s.DescriptionColor, false, v => s.DescriptionColor = v, Widgets.Default(s).DescriptionColor);
            Color("Details color (ID, who applied it)", s.DetailColor, false, v => s.DetailColor = v, Widgets.Default(s).DetailColor);
            Color("Background color", s.BackgroundColor, true, v => s.BackgroundColor = v, Widgets.Default(s).BackgroundColor);
            Color("Border color", s.BorderColor, false, v => s.BorderColor = v, Widgets.Default(s).BorderColor);

            if (ImGui.Button("Reset appearance"))
            {
                var fresh = new TooltipSettings();
                s.TextSize = fresh.TextSize;
                s.MaxWidthPx = fresh.MaxWidthPx;
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

    // Font sizes are fixed steps (each one is a real font, so the text is always sharp), like the text of every other element.
    private void TextSizeCombo(TooltipSettings s)
    {
        if (!ImGui.BeginCombo("Text size (px)", $"{s.TextSize} px", ImGuiComboFlags.None))
            return;

        foreach (var size in OverlayFonts.AvailableSizes)
        {
            if (size < 10 || size > 24)
                continue;

            if (ImGui.Selectable($"{size} px", s.TextSize == size))
            {
                s.TextSize = size;
                plugin.RequestSave();
            }
        }

        ImGui.EndCombo();
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

    private void Color(string label, Vector4 current, bool alpha, Action<Vector4> apply, Vector4 original)
    {
        var value = current;
        if (Widgets.ColorSetting(label, ref value, original, alpha))
        {
            apply(value);
            plugin.RequestSave();
        }
    }
}
