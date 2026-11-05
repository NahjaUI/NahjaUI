using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.UnitFrames;
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
            case 37:
                DrawGunbreaker(plugin.Configuration.JobGauges.Gunbreaker);
                break;
            case 32:
                DrawDarkKnight(plugin.Configuration.JobGauges.DarkKnight);
                break;
            case 35:
                DrawRedMage(plugin.Configuration.JobGauges.RedMage);
                break;
            case 28:
                DrawScholar(plugin.Configuration.JobGauges.Scholar);
                break;
            case 33:
                DrawAstrologian(plugin.Configuration.JobGauges.Astrologian);
                break;
            case 40:
                DrawSage(plugin.Configuration.JobGauges.Sage);
                break;
            case 21:
                DrawBarGauge("Warrior", "war", plugin.Configuration.JobGauges.Warrior, plugin.JobGaugeSystem.WarriorElement, "Beast Gauge");
                break;
            default:
                if (ExtraGauges.ByJob.TryGetValue(Entries[shown].JobId, out var extra))
                    DrawExtra(extra);
                else
                    DrawWhiteMage(plugin.Configuration.JobGauges.WhiteMage);

                break;
        }
    }

    /// <summary>The "Limit Break" page: the gauge shared by every job.</summary>
    public void DrawLimitBreakPage()
    {
        plugin.JobGaugeSystem.PreviewJobId = JobGaugeModule.LimitBreakPageId;
        DrawLimitBreak(plugin.Configuration.JobGauges.LimitBreak);
    }

    /// <summary>The "Frontline gauge" page: the PvP gauge.</summary>
    public void DrawFrontlinePage()
    {
        plugin.JobGaugeSystem.PreviewJobId = JobGaugeModule.FrontlinePageId;
        DrawBarGauge("Frontline", "fl", plugin.Configuration.JobGauges.Frontline, plugin.JobGaugeSystem.FrontlineElement, "PvP gauge", false);
    }

    // The jobs that have a gauge, in the order of the game's job list. New jobs are added in their place.
    private static readonly (uint JobId, string Name)[] Entries =
    {
        (19, "Paladin"), (21, "Warrior"), (32, "Dark Knight"), (37, "Gunbreaker"), (24, "White Mage"), (28, "Scholar"), (33, "Astrologian"), (40, "Sage"),
        (20, "Monk"), (22, "Dragoon"), (30, "Ninja"), (34, "Samurai"), (39, "Reaper"), (41, "Viper"),
        (23, "Bard"), (31, "Machinist"), (38, "Dancer"),
        (25, "Black Mage"), (27, "Summoner"), (35, "Red Mage"), (42, "Pictomancer")
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
            Widgets.EnabledHint();

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

            settings.EnsureText();
            ImGui.PushID("gaugeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

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

    private void DrawLimitBreak(LimitBreakGaugeSettings settings)
    {
        const string id = "lb";
        Widgets.Card("##card_gauge_lb", 20, () =>
        {
            Widgets.SectionLabel("Limit Break (the party's gauge, every job)");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox($"Enabled##{id}", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }

            var showLevel = settings.ShowLevel;
            if (ImGui.Checkbox($"Write the level (LB1, LB2, LB3) when a bar is full##{id}", ref showLevel))
            {
                settings.ShowLevel = showLevel;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            SizeControls("Gauge", plugin.JobGaugeSystem.LimitBreakElement.Layout);

            ImGui.Spacing();

            var gap = settings.GapPx;
            if (ImGui.SliderInt($"Space between bars (px)##{id}", ref gap, 0, 20))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            settings.EnsureText();
            ImGui.PushID("gaugeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            var fill = settings.FillColor;
            if (ImGui.ColorEdit4($"Filling bar color##{id}", ref fill, ImGuiColorEditFlags.NoAlpha))
            {
                settings.FillColor = new Vector4(fill.X, fill.Y, fill.Z, 1f);
                plugin.RequestSave();
            }

            var full = settings.FullColor;
            if (ImGui.ColorEdit4($"Full bar color##{id}", ref full, ImGuiColorEditFlags.NoAlpha))
            {
                settings.FullColor = new Vector4(full.X, full.Y, full.Z, 1f);
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
        });
    }

    // The small helpers of the healer cards.
    private void Check(string label, bool value, Action<bool> set)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            plugin.RequestSave();
        }
    }

    private void Slider(string label, int value, int max, Action<int> set)
    {
        if (ImGui.SliderInt(label, ref value, 0, max))
        {
            set(value);
            plugin.RequestSave();
        }
    }

    private void Color(string label, Vector4 value, bool alpha, Action<Vector4> set)
    {
        if (ImGui.ColorEdit4(label, ref value, alpha ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoAlpha))
        {
            set(alpha ? value : new Vector4(value.X, value.Y, value.Z, 1f));
            plugin.RequestSave();
        }
    }

    private void DrawScholar(ScholarGaugeSettings settings)
    {
        Widgets.Card("##card_gauge_sch", 22, () =>
        {
            Widgets.SectionLabel("Scholar (Aetherflow, Faerie Gauge)");

            Check("Enabled##sch", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
            Check("Write the Faerie Gauge (or the seconds of the Seraph) on the bar##sch", settings.ShowNumber, v => settings.ShowNumber = v);

            ImGui.Spacing();
            SizeControls("Aetherflow", plugin.JobGaugeSystem.AetherflowElement.Layout);
            SizeControls("Faerie Gauge", plugin.JobGaugeSystem.FaerieElement.Layout);

            ImGui.Spacing();
            Slider("Space between pips (px)##sch", settings.GapPx, 20, v => settings.GapPx = v);
            Slider("Corner rounding (px)##sch", settings.RoundingPx, 12, v => settings.RoundingPx = v);

            ImGui.Spacing();
            ImGui.PushID("schText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            Color("Aetherflow color##sch", settings.AetherflowColor, false, v => settings.AetherflowColor = v);
            Color("Faerie Gauge color##sch", settings.FaerieColor, false, v => settings.FaerieColor = v);
            Color("Seraph color##sch", settings.SeraphColor, false, v => settings.SeraphColor = v);
            Color("Background color##sch", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Color("Border color##sch", settings.BorderColor, false, v => settings.BorderColor = v);
        });
    }

    private void DrawAstrologian(AstrologianGaugeSettings settings)
    {
        Widgets.Card("##card_gauge_ast", 20, () =>
        {
            Widgets.SectionLabel("Astrologian (Cards, Crown card)");

            Check("Enabled##ast", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
            Check("Show the Crown card (Lord or Lady of Crowns)##ast", settings.ShowCrown, v => settings.ShowCrown = v);

            ImGui.Spacing();
            SizeControls("Cards", plugin.JobGaugeSystem.CardsElement.Layout);
            SizeControls("Crown card", plugin.JobGaugeSystem.CrownElement.Layout);

            ImGui.Spacing();
            Slider("Space between cards (px)##ast", settings.GapPx, 20, v => settings.GapPx = v);
            Slider("Corner rounding (px)##ast", settings.RoundingPx, 12, v => settings.RoundingPx = v);

            ImGui.Spacing();
            Color("Astral Draw color##ast", settings.AstralColor, false, v => settings.AstralColor = v);
            Color("Umbral Draw color##ast", settings.UmbralColor, false, v => settings.UmbralColor = v);
            Color("Lord of Crowns color##ast", settings.LordColor, false, v => settings.LordColor = v);
            Color("Lady of Crowns color##ast", settings.LadyColor, false, v => settings.LadyColor = v);
            Color("Background color##ast", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Color("Border color##ast", settings.BorderColor, false, v => settings.BorderColor = v);
        });
    }

    // The page of a job with a generic gauge (melee, physical ranged and casters): the same controls for all of them, one block for each row.
    private void DrawExtra(ExtraGaugeDef def)
    {
        var settings = plugin.Configuration.JobGauges.Extra.For(def.Abbr);
        var id = def.Abbr.ToLowerInvariant();
        var elements = plugin.JobGaugeSystem.ElementsOf(def.Abbr);

        Widgets.Card($"##card_gauge_{id}", 10 + def.Parts.Length * 6, () =>
        {
            Widgets.SectionLabel($"{def.Name} ({string.Join(", ", def.Parts.Select(part => part.Label))})");

            Check($"Enabled##{id}", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();

            for (var i = 0; i < def.Parts.Length; i++)
            {
                var part = def.Parts[i];
                var partSettings = settings.Part(part.Id);

                ImGui.Spacing();
                ImGui.TextDisabled(part.Label);
                Check($"Show##{id}{part.Id}", partSettings.Show, v => partSettings.Show = v);
                SizeControls(part.Label, elements[i].Layout);

                for (var c = 0; c < part.Colors.Length; c++)
                {
                    var index = c;
                    Color($"{part.ColorNames[c]} color##{id}{part.Id}{c}", settings.ColorOf(part, c), false, v => partSettings.Colors[index] = v);
                }
            }

            ImGui.Spacing();
            Slider($"Space between pips (px)##{id}", settings.GapPx, 20, v => settings.GapPx = v);
            Slider($"Corner rounding (px)##{id}", settings.RoundingPx, 12, v => settings.RoundingPx = v);

            ImGui.Spacing();
            ImGui.PushID($"{id}Text");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            Color($"Background color##{id}", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Color($"Border color##{id}", settings.BorderColor, false, v => settings.BorderColor = v);
            Color($"Mark color##{id}", settings.MarkColor, true, v => settings.MarkColor = v);
        });
    }

    private void DrawSage(SageGaugeSettings settings)
    {
        Widgets.Card("##card_gauge_sge", 22, () =>
        {
            Widgets.SectionLabel("Sage (Addersgall, Addersting)");

            Check("Enabled##sge", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
            Check("Show the Addersting##sge", settings.ShowAddersting, v => settings.ShowAddersting = v);
            Check("Show the seconds until the next Addersgall##sge", settings.ShowTimer, v => settings.ShowTimer = v);

            ImGui.Spacing();
            SizeControls("Addersgall", plugin.JobGaugeSystem.AddersgallElement.Layout);
            SizeControls("Addersting", plugin.JobGaugeSystem.AdderstingElement.Layout);

            ImGui.Spacing();
            Slider("Space between pips (px)##sge", settings.GapPx, 20, v => settings.GapPx = v);
            Slider("Corner rounding (px)##sge", settings.RoundingPx, 12, v => settings.RoundingPx = v);

            ImGui.Spacing();
            ImGui.PushID("sgeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            Color("Addersgall color##sge", settings.AddersgallColor, false, v => settings.AddersgallColor = v);
            Color("Addersting color##sge", settings.AdderstingColor, false, v => settings.AdderstingColor = v);
            Color("Background color##sge", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Color("Border color##sge", settings.BorderColor, false, v => settings.BorderColor = v);
        });
    }

    private void DrawGunbreaker(GunbreakerGaugeSettings settings)
    {
        const string id = "gnb";
        Widgets.Card("##card_gauge_gnb", 12, () =>
        {
            Widgets.SectionLabel("Gunbreaker (Cartridges)");

            var enabled = settings.Enabled;
            if (ImGui.Checkbox($"Enabled##{id}", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }
            Widgets.EnabledHint();

            ImGui.Spacing();

            SizeControls("Cartridges", plugin.JobGaugeSystem.GunbreakerElement.Layout);

            ImGui.Spacing();

            var gap = settings.GapPx;
            if (ImGui.SliderInt($"Space between cartridges (px)##{id}", ref gap, 0, 20))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var color = settings.CartridgeColor;
            if (ImGui.ColorEdit4($"Cartridge color##{id}", ref color, ImGuiColorEditFlags.NoAlpha))
            {
                settings.CartridgeColor = new Vector4(color.X, color.Y, color.Z, 1f);
                plugin.RequestSave();
            }

            var background = settings.BackgroundColor;
            if (ImGui.ColorEdit4($"Empty cartridge color##{id}", ref background, ImGuiColorEditFlags.None))
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
        });
    }

    private void DrawDarkKnight(DarkKnightGaugeSettings settings)
    {
        const string id = "drk";
        Widgets.Card("##card_gauge_drk", 30, () =>
        {
            Widgets.SectionLabel("Dark Knight (Blood, Darkside)");

            void Check(string label, bool value, Action<bool> set)
            {
                if (ImGui.Checkbox($"{label}##{id}", ref value))
                {
                    set(value);
                    plugin.RequestSave();
                }
            }

            void Pick(string label, Vector4 value, bool alpha, Action<Vector4> set)
            {
                if (ImGui.ColorEdit4($"{label}##{id}", ref value, alpha ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoAlpha))
                {
                    set(alpha ? value : new Vector4(value.X, value.Y, value.Z, 1f));
                    plugin.RequestSave();
                }
            }

            Check("Enabled", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
            Check("Show the number on the Blood bar", settings.ShowNumber, v => settings.ShowNumber = v);
            Check("Show a mark at 50", settings.ShowThreshold, v => settings.ShowThreshold = v);
            Check("Change color from 50 up (a spender is ready)", settings.HighlightWhenReady, v => settings.HighlightWhenReady = v);
            Check("Show the Darkside bar", settings.ShowDarkside, v => settings.ShowDarkside = v);
            Check("Show the seconds on the Darkside bar", settings.ShowDarksideSeconds, v => settings.ShowDarksideSeconds = v);
            Check("Frame the Darkside bar while Dark Arts is active", settings.ShowDarkArts, v => settings.ShowDarkArts = v);
            Check("Show the seconds of Living Shadow on the Darkside bar", settings.ShowLivingShadow, v => settings.ShowLivingShadow = v);

            ImGui.Spacing();

            SizeControls("Blood bar", plugin.JobGaugeSystem.DarkBloodElement.Layout);
            SizeControls("Darkside bar", plugin.JobGaugeSystem.DarksideElement.Layout);

            ImGui.Spacing();

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            settings.EnsureText();
            ImGui.PushID("gaugeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            if (settings.ShowLivingShadow)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, "Living Shadow seconds");
                ImGui.PushID("livingShadowText");
                if (UnitFrameEditor.TextControls(settings.LivingShadowText))
                    plugin.RequestSave();
                ImGui.PopID();
            }

            ImGui.Spacing();

            Pick("Blood color", settings.BarColor, false, v => settings.BarColor = v);
            Pick("Blood color from 50 up", settings.ReadyColor, false, v => settings.ReadyColor = v);
            Pick("Darkside color", settings.DarksideColor, false, v => settings.DarksideColor = v);
            Pick("Dark Arts frame color", settings.DarkArtsColor, false, v => settings.DarkArtsColor = v);
            Pick("Empty bar color", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Pick("Border color", settings.BorderColor, false, v => settings.BorderColor = v);
            Pick("Mark color", settings.ThresholdColor, true, v => settings.ThresholdColor = v);
        });
    }

    private void DrawRedMage(RedMageGaugeSettings settings)
    {
        const string id = "rdm";
        Widgets.Card("##card_gauge_rdm", 26, () =>
        {
            Widgets.SectionLabel("Red Mage (White Mana, Black Mana, Mana Stacks)");

            void Check(string label, bool value, Action<bool> set)
            {
                if (ImGui.Checkbox($"{label}##{id}", ref value))
                {
                    set(value);
                    plugin.RequestSave();
                }
            }

            void Pick(string label, Vector4 value, bool alpha, Action<Vector4> set)
            {
                if (ImGui.ColorEdit4($"{label}##{id}", ref value, alpha ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoAlpha))
                {
                    set(alpha ? value : new Vector4(value.X, value.Y, value.Z, 1f));
                    plugin.RequestSave();
                }
            }

            Check("Enabled", settings.Enabled, v => settings.Enabled = v);
            Widgets.EnabledHint();
            Check("Show the number on the bar", settings.ShowNumbers, v => settings.ShowNumbers = v);
            Check("Show a mark at 50", settings.ShowThreshold, v => settings.ShowThreshold = v);
            Check("Show the Mana Stacks", settings.ShowStacks, v => settings.ShowStacks = v);
            Check("Frame the bars when both have 50 or more", settings.FrameWhenReady, v => settings.FrameWhenReady = v);

            ImGui.Spacing();

            SizeControls("White Mana", plugin.JobGaugeSystem.RedWhiteElement.Layout);
            SizeControls("Black Mana", plugin.JobGaugeSystem.RedBlackElement.Layout);
            SizeControls("Mana Stacks", plugin.JobGaugeSystem.RedStackElement.Layout);

            ImGui.Spacing();

            var gap = settings.GapPx;
            if (ImGui.SliderInt($"Space between stacks (px)##{id}", ref gap, 0, 20))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt($"Corner rounding (px)##{id}", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            settings.EnsureText();
            ImGui.PushID("gaugeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            Pick("White Mana color", settings.WhiteColor, false, v => settings.WhiteColor = v);
            Pick("Black Mana color", settings.BlackColor, false, v => settings.BlackColor = v);
            Pick("Mana Stack color", settings.StackColor, false, v => settings.StackColor = v);
            Pick("Ready frame color", settings.ReadyFrameColor, false, v => settings.ReadyFrameColor = v);
            Pick("Background color", settings.BackgroundColor, true, v => settings.BackgroundColor = v);
            Pick("Border color", settings.BorderColor, false, v => settings.BorderColor = v);
            Pick("Mark color", settings.ThresholdColor, true, v => settings.ThresholdColor = v);
        });
    }

    // The card of a job whose gauge is one bar from 0 to 100 (Warrior, Paladin). <id> keeps the control ids apart.
    private void DrawBarGauge(string title, string id, BarGaugeSettings settings, LayoutElement element, string gaugeName, bool hasThreshold = true)
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

            var showNumber = settings.ShowNumber;
            if (ImGui.Checkbox($"Show the number on the bar##{id}", ref showNumber))
            {
                settings.ShowNumber = showNumber;
                plugin.RequestSave();
            }

            if (hasThreshold)
            {
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

            settings.EnsureText();
            ImGui.PushID("gaugeText");
            if (UnitFrameEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            var bar = settings.BarColor;
            if (ImGui.ColorEdit4($"Bar color##{id}", ref bar, ImGuiColorEditFlags.NoAlpha))
            {
                settings.BarColor = new Vector4(bar.X, bar.Y, bar.Z, 1f);
                plugin.RequestSave();
            }

            if (hasThreshold)
            {
                var ready = settings.ReadyColor;
                if (ImGui.ColorEdit4($"Color from 50 up##{id}", ref ready, ImGuiColorEditFlags.NoAlpha))
                {
                    settings.ReadyColor = new Vector4(ready.X, ready.Y, ready.Z, 1f);
                    plugin.RequestSave();
                }
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

            if (hasThreshold)
            {
                var mark = settings.ThresholdColor;
                if (ImGui.ColorEdit4($"Mark color##{id}", ref mark, ImGuiColorEditFlags.None))
                {
                    settings.ThresholdColor = mark;
                    plugin.RequestSave();
                }
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
        OverlayFont.NahjaDisplay => "NahjaUI Display",
        OverlayFont.NahjaDisplayBold => "NahjaUI Numbers",
        OverlayFont.NahjaText => "NahjaUI Text",
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
