using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Modules.HudBars;
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
        DrawLimitBreak(plugin.Configuration.JobGauges.LimitBreak, plugin.JobGaugeSystem.LimitBreakElement, "lb", "Limit Break");
    }

    /// <summary>The "Limit Break (PvP)" page: the gauge of the game's PvP matches, with its own position and look.</summary>
    public void DrawLimitBreakPvpPage()
    {
        plugin.JobGaugeSystem.PreviewJobId = JobGaugeModule.LimitBreakPvpPageId;
        DrawLimitBreak(plugin.Configuration.JobGauges.LimitBreakPvp, plugin.JobGaugeSystem.LimitBreakPvpElement, "lbpvp", "Limit Break (PvP)");
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
            if (ImGui.SliderInt("Space between pips (px)", ref gap, 0, 30))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            ImGui.PushID("gaugeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            var lily = settings.LilyColor;
            if (Widgets.ColorSetting("Lily color", ref lily, Widgets.Default(settings).LilyColor))
            {
                settings.LilyColor = lily;
                plugin.RequestSave();
            }

            var blood = settings.BloodLilyColor;
            if (Widgets.ColorSetting("Blood lily color", ref blood, Widgets.Default(settings).BloodLilyColor))
            {
                settings.BloodLilyColor = blood;
                plugin.RequestSave();
            }

            var glowColor = settings.GlowColor;
            if (Widgets.ColorSetting("Full frame color", ref glowColor, Widgets.Default(settings).GlowColor))
            {
                settings.GlowColor = glowColor;
                plugin.RequestSave();
            }

            if (HudBarEditor.BackgroundAndBorder("bgbd1", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
        });
    }

    private void DrawLimitBreak(LimitBreakGaugeSettings settings, LayoutElement element, string id, string title)
    {
        Widgets.Card($"##card_gauge_{id}", 21, () =>
        {
            Widgets.SectionLabel(title);

            var enabled = settings.Enabled;
            if (ImGui.Checkbox($"Enabled##{id}", ref enabled))
            {
                settings.Enabled = enabled;
                plugin.RequestSave();
            }
            Widgets.EnabledHint();

            var showLevel = settings.ShowLevel;
            if (ImGui.Checkbox($"Write the level (LB1, LB2, LB3) when a bar is full##{id}", ref showLevel))
            {
                settings.ShowLevel = showLevel;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            SizeControls("Gauge", element.Layout);

            ImGui.Spacing();

            var gap = settings.GapPx;
            if (ImGui.SliderInt($"Space between bars (px)##{id}", ref gap, 0, 30))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            ImGui.PushID("gaugeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            var fill = settings.FillColor;
            if (Widgets.ColorSetting($"Filling bar color##{id}", ref fill, Widgets.Default(settings).FillColor))
            {
                settings.FillColor = fill;
                plugin.RequestSave();
            }

            var full = settings.FullColor;
            if (Widgets.ColorSetting($"Full bar color##{id}", ref full, Widgets.Default(settings).FullColor))
            {
                settings.FullColor = full;
                plugin.RequestSave();
            }

            var readyFrame = settings.ReadyFrameColor;
            if (Widgets.ColorSetting($"Border of a full bar##{id}", ref readyFrame, Widgets.Default(settings).ReadyFrameColor))
            {
                settings.ReadyFrameColor = readyFrame;
                plugin.RequestSave();
            }

            if (HudBarEditor.BackgroundAndBorder($"bgbd2_{id}", "Empty bar color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
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

    private void Color(string label, Vector4 value, bool alpha, Action<Vector4> set, Vector4 original)
    {
        if (Widgets.ColorSetting(label, ref value, original, alpha))
        {
            set(value);
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
            ImGui.Spacing();
            ImGui.PushID("schText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            Color("Aetherflow color##sch", settings.AetherflowColor, false, v => settings.AetherflowColor = v, Widgets.Default(settings).AetherflowColor);
            Color("Faerie Gauge color##sch", settings.FaerieColor, false, v => settings.FaerieColor = v, Widgets.Default(settings).FaerieColor);
            Color("Seraph color##sch", settings.SeraphColor, false, v => settings.SeraphColor = v, Widgets.Default(settings).SeraphColor);
            if (HudBarEditor.BackgroundAndBorder("bgbd5", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
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
            ImGui.Spacing();
            Color("Astral Draw color##ast", settings.AstralColor, false, v => settings.AstralColor = v, Widgets.Default(settings).AstralColor);
            Color("Umbral Draw color##ast", settings.UmbralColor, false, v => settings.UmbralColor = v, Widgets.Default(settings).UmbralColor);
            Color("Lord of Crowns color##ast", settings.LordColor, false, v => settings.LordColor = v, Widgets.Default(settings).LordColor);
            Color("Lady of Crowns color##ast", settings.LadyColor, false, v => settings.LadyColor = v, Widgets.Default(settings).LadyColor);
            if (HudBarEditor.BackgroundAndBorder("bgbd6", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
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
                    Color($"{part.ColorNames[c]} color##{id}{part.Id}{c}", settings.ColorOf(part, c), false, v => partSettings.Colors[index] = v, part.Colors[c]);
                }
            }

            ImGui.Spacing();
            Slider($"Space between pips (px)##{id}", settings.GapPx, 20, v => settings.GapPx = v);
            ImGui.Spacing();
            ImGui.PushID($"{id}Text");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            if (HudBarEditor.BackgroundAndBorder($"bgbd7_{id}", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
            Color($"Mark color##{id}", settings.MarkColor, true, v => settings.MarkColor = v, Widgets.Default(settings).MarkColor);
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
            ImGui.Spacing();
            ImGui.PushID("sgeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();
            Color("Addersgall color##sge", settings.AddersgallColor, false, v => settings.AddersgallColor = v, Widgets.Default(settings).AddersgallColor);
            Color("Addersting color##sge", settings.AdderstingColor, false, v => settings.AdderstingColor = v, Widgets.Default(settings).AdderstingColor);
            if (HudBarEditor.BackgroundAndBorder("bgbd8", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
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
            if (ImGui.SliderInt($"Space between cartridges (px)##{id}", ref gap, 0, 30))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            var color = settings.CartridgeColor;
            if (Widgets.ColorSetting($"Cartridge color##{id}", ref color, Widgets.Default(settings).CartridgeColor))
            {
                settings.CartridgeColor = color;
                plugin.RequestSave();
            }

            if (HudBarEditor.BackgroundAndBorder($"bgbd3_{id}", "Empty cartridge color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
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

            void Pick(string label, Vector4 value, bool alpha, Action<Vector4> set, Vector4 original)
            {
                if (Widgets.ColorSetting($"{label}##{id}", ref value, original, alpha))
                {
                    set(value);
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

            ImGui.Spacing();

            ImGui.TextColored(Theme.TextMuted, "Blood bar number");
            ImGui.PushID("gaugeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            if (settings.ShowDarksideSeconds)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, "Darkside bar seconds");
                ImGui.PushID("darksideText");
                if (HudBarEditor.TextControls(settings.DarksideText))
                    plugin.RequestSave();
                ImGui.PopID();
            }

            if (settings.ShowLivingShadow)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.TextMuted, "Living Shadow seconds");
                ImGui.PushID("livingShadowText");
                if (HudBarEditor.TextControls(settings.LivingShadowText, Widgets.Default(settings).LivingShadowText.Color))
                    plugin.RequestSave();
                ImGui.PopID();
            }

            ImGui.Spacing();

            Pick("Blood color", settings.BarColor, false, v => settings.BarColor = v, Widgets.Default(settings).BarColor);
            Pick("Blood color from 50 up", settings.ReadyColor, false, v => settings.ReadyColor = v, Widgets.Default(settings).ReadyColor);
            Pick("Darkside color", settings.DarksideColor, false, v => settings.DarksideColor = v, Widgets.Default(settings).DarksideColor);
            Pick("Dark Arts frame color", settings.DarkArtsColor, false, v => settings.DarkArtsColor = v, Widgets.Default(settings).DarkArtsColor);
            if (HudBarEditor.BackgroundAndBorder($"bgbd9_{id}", "Empty bar color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
            Pick("Mark color", settings.ThresholdColor, true, v => settings.ThresholdColor = v, Widgets.Default(settings).ThresholdColor);
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

            void Pick(string label, Vector4 value, bool alpha, Action<Vector4> set, Vector4 original)
            {
                if (Widgets.ColorSetting($"{label}##{id}", ref value, original, alpha))
                {
                    set(value);
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
            if (ImGui.SliderInt($"Space between stacks (px)##{id}", ref gap, 0, 30))
            {
                settings.GapPx = gap;
                plugin.RequestSave();
            }

            ImGui.Spacing();

            ImGui.PushID("gaugeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            Pick("White Mana color", settings.WhiteColor, false, v => settings.WhiteColor = v, Widgets.Default(settings).WhiteColor);
            Pick("Black Mana color", settings.BlackColor, false, v => settings.BlackColor = v, Widgets.Default(settings).BlackColor);
            Pick("Mana Stack color", settings.StackColor, false, v => settings.StackColor = v, Widgets.Default(settings).StackColor);
            Pick("Ready frame color", settings.ReadyFrameColor, false, v => settings.ReadyFrameColor = v, Widgets.Default(settings).ReadyFrameColor);
            if (HudBarEditor.BackgroundAndBorder($"bgbd10_{id}", "Background color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();
            Pick("Mark color", settings.ThresholdColor, true, v => settings.ThresholdColor = v, Widgets.Default(settings).ThresholdColor);
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
            Widgets.EnabledHint();

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

            ImGui.Spacing();

            ImGui.PushID("gaugeText");
            if (HudBarEditor.TextControls(settings.Text))
                plugin.RequestSave();
            ImGui.PopID();

            ImGui.Spacing();

            var bar = settings.BarColor;
            if (Widgets.ColorSetting($"Bar color##{id}", ref bar, Widgets.Default(settings).BarColor))
            {
                settings.BarColor = bar;
                plugin.RequestSave();
            }

            if (hasThreshold)
            {
                var ready = settings.ReadyColor;
                if (Widgets.ColorSetting($"Color from 50 up##{id}", ref ready, Widgets.Default(settings).ReadyColor))
                {
                    settings.ReadyColor = ready;
                    plugin.RequestSave();
                }
            }

            if (HudBarEditor.BackgroundAndBorder($"bgbd4_{id}", "Empty bar color", "Border color",
                settings.CustomBackground, settings.BackgroundColor, onBg => settings.CustomBackground = onBg, colBg => settings.BackgroundColor = colBg,
                settings.CustomBorder, settings.BorderColor, onBd => settings.CustomBorder = onBd, colBd => settings.BorderColor = colBd))
                plugin.RequestSave();

            if (hasThreshold)
            {
                var mark = settings.ThresholdColor;
                if (Widgets.ColorSetting($"Mark color##{id}", ref mark, Widgets.Default(settings).ThresholdColor, true))
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
