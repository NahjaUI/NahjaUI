using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

namespace NahjaUI.Modules.JobGauges;

/// <summary>The "Job gauges" page of the main window: one card per job (White Mage and Warrior for now).</summary>
internal sealed class JobGaugeEditor
{
    private readonly Plugin plugin;

    public JobGaugeEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {

        // Cards follow the order of the game's job list: tanks (PLD, WAR, DRK, GNB), healers (WHM, SCH, AST, SGE),
        // melee (MNK, DRG, NIN, SAM, RPR, VPR, BST), physical ranged (BRD, MCH, DNC), magical ranged (BLM, SMN, RDM, PCT, BLU).
        // New jobs are added in their place in this list.
        var current = plugin.GameData.Current.Player?.JobId ?? 0;
        var currentEntry = Array.FindIndex(Entries, entry => entry.JobId == current);

        // By default the gauge of the job being played. The combo lets the user edit another job's gauge meanwhile.
        var shown = chosen >= 0 ? chosen : Math.Max(currentEntry, 0);

        if (ImGui.BeginCombo("Job", Entries[shown].Name))
        {
            for (var i = 0; i < Entries.Length; i++)
            {
                var label = i == currentEntry ? $"{Entries[i].Name} (current)" : Entries[i].Name;
                if (ImGui.Selectable(label, i == shown))
                    chosen = i == currentEntry ? -1 : i;
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // While this page is open, the gauge being edited (only that one) shows sample values, so it can be adjusted without playing the job.
        plugin.JobGaugeSystem.PreviewJobId = Entries[shown].JobId;

        switch (Entries[shown].JobId)
        {
            case 19:
                DrawBarGauge("Paladin", "pld", plugin.Configuration.JobGauges.Paladin, plugin.JobGaugeSystem.PaladinElement, "Oath Gauge");
                break;
            case 21:
                DrawBarGauge("Warrior", "war", plugin.Configuration.JobGauges.Warrior, plugin.JobGaugeSystem.WarriorElement, "Beast Gauge");
                break;
            default:
                DrawWhiteMage(plugin.Configuration.JobGauges.WhiteMage);
                break;
        }
    }

    // The jobs that have a gauge, in the order of the game's job list. New jobs are added in their place.
    private static readonly (uint JobId, string Name)[] Entries =
    {
        (19, "Paladin"), (21, "Warrior"), (24, "White Mage")
    };

    // The job picked by hand (index into Entries), or -1 to follow the job being played.
    private int chosen = -1;

    private void DrawWhiteMage(WhiteMageGaugeSettings settings)
    {
        Widgets.Card("##card_gauge_whm", 25, () =>
        {
            Widgets.SectionLabel("White Mage");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var hideNative = settings.HideNative;
            if (ImGui.Checkbox("Hide the game's gauge", ref hideNative))
            {
                settings.HideNative = hideNative;
                plugin.RequestSave();
            }

            var showBlood = settings.ShowBloodLilies;
            if (ImGui.Checkbox("Show the blood lilies", ref showBlood))
            {
                settings.ShowBloodLilies = showBlood;
                plugin.RequestSave();
            }

            var showTimer = settings.ShowTimer;
            if (ImGui.Checkbox("Show the seconds until the next lily", ref showTimer))
            {
                settings.ShowTimer = showTimer;
                plugin.RequestSave();
            }

            var glow = settings.GlowWhenFull;
            if (ImGui.Checkbox("Frame the blood lilies when all three are full", ref glow))
            {
                settings.GlowWhenFull = glow;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            // The size of each row belongs to the layout system, like its position.
            SizeControls("Lilies", plugin.JobGaugeSystem.LilyElement.Layout);
            SizeControls("Blood lilies", plugin.JobGaugeSystem.BloodLilyElement.Layout);

            ImGui.Spacing();

            var gap = settings.GapPx;
            if (ImGui.SliderInt("Space between pips (px)", ref gap, 0, 20))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
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

            var lily = settings.LilyColor;
            if (ImGui.ColorEdit4("Lily color", ref lily, ImGuiColorEditFlags.NoAlpha))
            {
                settings.LilyColor = new Vector4(lily.X, lily.Y, lily.Z, 1f);
                plugin.RequestSave();
            }

            var blood = settings.BloodLilyColor;
            if (ImGui.ColorEdit4("Blood lily color", ref blood, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BloodLilyColor = new Vector4(blood.X, blood.Y, blood.Z, 1f);
                plugin.RequestSave();
            }

            var glowColor = settings.GlowColor;
            if (ImGui.ColorEdit4("Full frame color", ref glowColor, ImGuiColorEditFlags.NoAlpha))
            {
                settings.GlowColor = new Vector4(glowColor.X, glowColor.Y, glowColor.Z, 1f);
                plugin.RequestSave();
            }

            var background = settings.BackgroundColor;
            if (ImGui.ColorEdit4("Empty pip color", ref background, ImGuiColorEditFlags.None))
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

    // The card of a job whose gauge is one bar from 0 to 100 (Warrior, Paladin). <id> keeps the control ids apart.
    private void DrawBarGauge(string title, string id, BarGaugeSettings settings, LayoutElement element, string gaugeName)
    {
        Widgets.Card($"##card_gauge_{id}", 22, () =>
        {
            Widgets.SectionLabel($"{title} ({gaugeName})");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox($"Enabled##{id}", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var hideNative = settings.HideNative;
            if (ImGui.Checkbox($"Hide the game's gauge##{id}", ref hideNative))
            {
                settings.HideNative = hideNative;
                plugin.RequestSave();
            }

            var showNumber = settings.ShowNumber;
            if (ImGui.Checkbox($"Show the number on the bar##{id}", ref showNumber))
            {
                settings.ShowNumber = showNumber;
                plugin.RequestSave();
            }

            var showThreshold = settings.ShowThreshold;
            if (ImGui.Checkbox($"Show a mark at 50##{id}", ref showThreshold))
            {
                settings.ShowThreshold = showThreshold;
                plugin.RequestSave();
            }

            var highlight = settings.HighlightWhenReady;
            if (ImGui.Checkbox($"Change color from 50 up (a spender is ready)##{id}", ref highlight))
            {
                settings.HighlightWhenReady = highlight;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            SizeControls("Bar", element.Layout);

            ImGui.Spacing();

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            if (ImGui.BeginCombo($"Text size (px)##{id}", $"{settings.TextSize} px", ImGuiComboFlags.None))
            {
                foreach (var size in OverlayFonts.AvailableSizes)
                {
                    if (ImGui.Selectable($"{size} px##{id}", settings.TextSize == size))
                    {
                        settings.TextSize = size;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var font = settings.Font;
            if (EnumCombo($"Text font##{id}", ref font, FontLabel))
            {
                settings.Font = font;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var bar = settings.BarColor;
            if (ImGui.ColorEdit4($"Bar color##{id}", ref bar, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BarColor = new Vector4(bar.X, bar.Y, bar.Z, 1f);
                plugin.RequestSave();
            }

            var ready = settings.ReadyColor;
            if (ImGui.ColorEdit4($"Color from 50 up##{id}", ref ready, ImGuiColorEditFlags.NoAlpha))
            {
                settings.ReadyColor = new Vector4(ready.X, ready.Y, ready.Z, 1f);
                plugin.RequestSave();
            }

            var background = settings.BackgroundColor;
            if (ImGui.ColorEdit4($"Empty bar color##{id}", ref background, ImGuiColorEditFlags.None))
            {
                settings.BackgroundColor = background;
                plugin.RequestSave();
            }

            var border = settings.BorderColor;
            if (ImGui.ColorEdit4($"Border color##{id}", ref border, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BorderColor = new Vector4(border.X, border.Y, border.Z, 1f);
                plugin.RequestSave();
            }

            var mark = settings.ThresholdColor;
            if (ImGui.ColorEdit4($"Mark color##{id}", ref mark, ImGuiColorEditFlags.None))
            {
                settings.ThresholdColor = mark;
                plugin.RequestSave();
            }
        });
    }

    // Width and height of one row. They are stored in the layout element, so unlock mode and this page agree.
    private void SizeControls(string rowName, ElementLayout layout)
    {
        var width = layout.Width;
        if (ImGui.InputInt($"{rowName} width (px)", ref width, 2, 10))
        {
            layout.Width = Math.Clamp(width, 1, 4000);
            plugin.RequestSave();
        }

        var height = layout.Height;
        if (ImGui.InputInt($"{rowName} height (px)", ref height, 2, 10))
        {
            layout.Height = Math.Clamp(height, 1, 1000);
            plugin.RequestSave();
        }
    }

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
