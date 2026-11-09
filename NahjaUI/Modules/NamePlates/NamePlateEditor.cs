using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;
using NahjaUI.Game;
using NahjaUI.Modules.HudBars;

namespace NahjaUI.Modules.NamePlates;

/// <summary>The "Nameplates" page of the main window: enemies and players.</summary>
internal sealed class NamePlateEditor
{
    private readonly Plugin plugin;

    public NamePlateEditor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        var master = plugin.Configuration.NamePlates;

        Widgets.Card("##card_np_general", 6, () =>
        {
            Widgets.SectionLabel("Nameplates");

            if (Check("Enabled", master.Enabled, out var enabled))
            {
                master.Enabled = enabled;
                Plugin.NamePlateGui.RequestRedraw();
            }
            Widgets.EnabledHint();

            if (Check("Always show the HP bar of my target", master.ShowBarOnTarget, out var barOnTarget))
                master.ShowBarOnTarget = barOnTarget;

        });

        if (!ImGui.BeginTabBar("##np_kinds"))
            return;

        DrawKind("Me", master.Self, NamePlateRelation.Self);
        DrawKind("Party", master.Party, NamePlateRelation.Party);
        DrawKind("Other players", master.OtherPlayers, NamePlateRelation.OtherPlayer);
        DrawKind("Friendly NPCs", master.FriendlyNpcs, NamePlateRelation.Friendly);
        DrawKind("Enemy NPCs", master.EnemyNpcs, NamePlateRelation.Enemy);
        DrawKind("Enemy players", master.EnemyPlayers, NamePlateRelation.EnemyPlayer);
        DrawKind("Others", master.OtherPlates, NamePlateRelation.Other);

        ImGui.EndTabBar();
    }

    // One tab of a kind of plate: its own switch, a button to copy another kind's settings, and the same three tabs for each.
    private void DrawKind(string title, NamePlateStyle style, NamePlateRelation kind)
    {
        if (!ImGui.BeginTabItem(title))
            return;

        ImGui.PushID($"kind{(int)kind}");

        Widgets.Card("##card_np_kind", 4, () =>
        {
            Widgets.SectionLabel(title);

            if (Check("Use NahjaUI plates for these", style.Active, out var active))
            {
                style.Active = active;
                Plugin.NamePlateGui.RequestRedraw();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Off: the game's own plate is left as it is.");

            DrawCopyButton(style, kind);
        });

        DrawStyle(style, kind);

        ImGui.PopID();
        ImGui.EndTabItem();
    }

    // "Copy settings from": copies everything of another kind of plate into this one (nothing stays linked).
    private void DrawCopyButton(NamePlateStyle target, NamePlateRelation kind)
    {
        var master = plugin.Configuration.NamePlates;
        var sources = new (string Label, NamePlateRelation Kind)[]
        {
            ("Me", NamePlateRelation.Self), ("Party", NamePlateRelation.Party), ("Other players", NamePlateRelation.OtherPlayer),
            ("Friendly NPCs", NamePlateRelation.Friendly), ("Enemy NPCs", NamePlateRelation.Enemy), ("Enemy players", NamePlateRelation.EnemyPlayer)
        };

        if (!ImGui.BeginCombo("Copy settings from", "Choose...", ImGuiComboFlags.None))
            return;

        foreach (var source in sources)
        {
            if (source.Kind == kind)
                continue;

            if (!ImGui.Selectable(source.Label))
                continue;

            // A copy through the saved form, so nothing is shared between the two (rows, filters, colors...).
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(master.StyleOf(source.Kind));
            var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<NamePlateStyle>(json);
            if (copy is null)
                continue;

            copy.Active = target.Active;
            master.SetStyle(kind, copy);
            plugin.RequestSave();
        }

        ImGui.EndCombo();
    }

    private void DrawStyle(NamePlateStyle settings, NamePlateRelation kind)
    {
        if (!ImGui.BeginTabBar("##np_sub_tabs"))
            return;

        if (ImGui.BeginTabItem("Plate"))
        {
        if (kind != NamePlateRelation.Other)
        Widgets.Card("##card_np_hp", 9, () =>
        {
            Widgets.SectionLabel("HP bar");

            settings.WidthPx = Number("Width (px)", settings.WidthPx, 1, 4000, 2, 10);
            settings.HeightPx = Number("Height (px)", settings.HeightPx, 1, 1000, 2, 10);
            ColorRow("Background color", settings.BarBackgroundColor, false, v => settings.BarBackgroundColor = v);
            ColorRow("Border color", settings.BarBorderColor, true, v => settings.BarBorderColor = v);
            ImGui.TextColored(Theme.TextMuted, "The color of the HP is the game's own (set in the game).");
        });

        Widgets.Card("##card_np_name", 17, () =>
        {
            Widgets.SectionLabel("Name");

            var anchor = settings.NameAnchor;
            if (EnumCombo("Position", ref anchor, point => point.Label()))
            {
                settings.NameAnchor = anchor;
                plugin.RequestSave();
            }

            settings.NameOffsetX = Number("Offset X (px)", settings.NameOffsetX, -300, 300, 1, 5);
            // Up is plus: the saved value counts downwards, so the sign is turned here.
            settings.NameOffsetY = -Number("Offset Y (px)", -settings.NameOffsetY, -300, 300, 1, 5);

            if (ImGui.BeginCombo("Text size (px)", $"{settings.NameSize} px", ImGuiComboFlags.None))
            {
                foreach (var size in OverlayFonts.AvailableSizes)
                {
                    if (ImGui.Selectable($"{size} px", settings.NameSize == size))
                    {
                        settings.NameSize = size;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var nameColor = settings.NameColor;
            if (ImGui.ColorEdit4("Text color", ref nameColor, ImGuiColorEditFlags.NoAlpha))
            {
                settings.NameColor = new Vector4(nameColor.X, nameColor.Y, nameColor.Z, 1f);
                plugin.RequestSave();
            }

            if (Check("Show the game's icon next to the name", settings.ShowNameIcon, out var showIcon))
                settings.ShowNameIcon = showIcon;

            settings.NameIconSizePx = Number("Icon size (px)", settings.NameIconSizePx, 8, 64, 1, 4);
            settings.NameIconGapPx = Number("Space icon - name (px)", settings.NameIconGapPx, 0, 30, 1, 2);

            if (Check("Show the game's icon above the plate", settings.ShowMarkerIcon, out var showMarker))
                settings.ShowMarkerIcon = showMarker;

            settings.MarkerIconSizePx = Number("Top icon size (px)", settings.MarkerIconSizePx, 12, 128, 1, 4);
            settings.MarkerOffsetX = Number("Top icon offset X (px)", settings.MarkerOffsetX, -300, 300, 1, 5);
            settings.MarkerOffsetY = -Number("Top icon offset Y (px)", -settings.MarkerOffsetY, -300, 300, 1, 5);

            // Which plates show a name and how it is written (full name, surname, initials...) are the game's own options.
            ImGui.TextColored(Theme.TextMuted, "When the name shows, how it is written and whether the title shows (it joins the name): set in the game.");
        });

        if (kind != NamePlateRelation.Other)
        Widgets.Card("##card_np_level", 6, () =>
        {
            Widgets.SectionLabel("Level");

            var levelMode = settings.LevelMode;
            if (EnumCombo("Level", ref levelMode, LevelModeLabel))
            {
                settings.LevelMode = levelMode;
                plugin.RequestSave();
            }

            var levelColor = settings.LevelColor;
            if (ImGui.ColorEdit4("Level color", ref levelColor, ImGuiColorEditFlags.NoAlpha))
            {
                settings.LevelColor = new Vector4(levelColor.X, levelColor.Y, levelColor.Z, 1f);
                plugin.RequestSave();
            }

            if (Check("Hide the level when it is the same as mine", settings.HideSameLevel, out var hideSame))
                settings.HideSameLevel = hideSame;

            ImGui.TextColored(Theme.TextMuted, "The level is written right after the name.");
        });

            ImGui.EndTabItem();
        }

        if (kind != NamePlateRelation.Other && ImGui.BeginTabItem("Progress bar"))
        {
        Widgets.Card("##card_np_progress_bar", 15, () =>
        {
            Widgets.SectionLabel("Progress bar");

            if (Check("Show the progress bar", settings.ShowProgressBar, out var showProgressBar))
                settings.ShowProgressBar = showProgressBar;

            var side = settings.ProgressBarPosition;
            if (EnumCombo("Progress bar position", ref side, option => option switch
                {
                    NahjaUI.Modules.EnmityList.ProgressBarSide.Right => "Right of the bar",
                    NahjaUI.Modules.EnmityList.ProgressBarSide.Left => "Left of the bar",
                    _ => "Under the bar"
                }))
            {
                settings.ProgressBarPosition = side;
                plugin.RequestSave();
            }

            var progressBarWidth = settings.ProgressBarWidthPx;
            if (ImGui.SliderInt("Progress bar width (px, 0 = same as the HP bar)", ref progressBarWidth, 0, 600))
            {
                settings.ProgressBarWidthPx = progressBarWidth;
                plugin.RequestSave();
            }

            var progressBarHeight = settings.ProgressBarHeightPx;
            if (ImGui.SliderInt("Progress bar height (px)", ref progressBarHeight, 6, 30))
            {
                settings.ProgressBarHeightPx = progressBarHeight;
                plugin.RequestSave();
            }

            var progressBarGap = settings.ProgressBarGapPx;
            if (ImGui.SliderInt("Space between HP bar and progress bar (px)", ref progressBarGap, 0, 40))
            {
                settings.ProgressBarGapPx = progressBarGap;
                plugin.RequestSave();
            }

            settings.ProgressBarOffsetX = Number("Progress bar offset X (px)", settings.ProgressBarOffsetX, -300, 300, 1, 5);
            // Up is plus: the saved value counts downwards, so the sign is turned here.
            settings.ProgressBarOffsetY = -Number("Progress bar offset Y (px)", -settings.ProgressBarOffsetY, -300, 300, 1, 5);

            ColorRow("Color when it can be interrupted", settings.ProgressBarColor, true, v => settings.ProgressBarColor = v);
            ColorRow("Color when it cannot be interrupted", settings.ProgressBarUninterruptibleColor, true, v => settings.ProgressBarUninterruptibleColor = v);
            ColorRow("Background color", settings.ProgressBarBackgroundColor, false, v => settings.ProgressBarBackgroundColor = v);
            ColorRow("Border color", settings.ProgressBarBorderColor, true, v => settings.ProgressBarBorderColor = v);
        });

        Widgets.Card("##card_np_progress_bar_name", 12, () =>
        {
            Widgets.SectionLabel("Progress bar: action name");

            if (Check("Show the action name", settings.ProgressBarName.Enabled, out var showName))
                settings.ProgressBarName.Enabled = showName;

            if (HudBarEditor.TextControls(settings.ProgressBarName))
                plugin.RequestSave();
        });

        Widgets.Card("##card_np_progress_bar_time", 12, () =>
        {
            Widgets.SectionLabel("Progress bar: time left");

            if (Check("Show the time left", settings.ProgressBarTime.Enabled, out var showTime))
                settings.ProgressBarTime.Enabled = showTime;

            var timeMode = settings.ProgressBarTimeMode;
            if (EnumCombo("Time format", ref timeMode, NahjaUI.Modules.ProgressBars.ProgressBarEditor.TimeModeLabel))
            {
                settings.ProgressBarTimeMode = timeMode;
                plugin.RequestSave();
            }

            if (HudBarEditor.TextControls(settings.ProgressBarTime))
                plugin.RequestSave();
        });

            ImGui.EndTabItem();
        }

        if (kind != NamePlateRelation.Other && ImGui.BeginTabItem("Status icons"))
        {
        plugin.HudBarEditorPage.DrawStatusRowCard("##card_np_enhancements", "Enhancements", settings.Enhancements, true, canDispel: !kind.IsEnemy());
        plugin.HudBarEditorPage.DrawStatusRowCard("##card_np_enfeeblements", "Enfeeblements", settings.Enfeeblements, false, canDispel: !kind.IsEnemy());

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // A color of the progress bar, with the same control as the Progress bars page (with or without the transparency slider).
    private void ColorRow(string label, Vector4 current, bool opaque, Action<Vector4> apply)
    {
        var value = current;
        if (ImGui.ColorEdit4(label, ref value, opaque ? ImGuiColorEditFlags.NoAlpha : ImGuiColorEditFlags.None))
        {
            apply(opaque ? new Vector4(value.X, value.Y, value.Z, 1f) : value);
            plugin.RequestSave();
        }
    }

    private static string LevelModeLabel(NamePlateLevelMode mode) => mode switch
    {
        NamePlateLevelMode.Hidden => "Do not show it",
        NamePlateLevelMode.NumberOnly => "Only the number (100)",
        NamePlateLevelMode.WithPrefix => "With the prefix (Lv 100)",
        _ => mode.ToString()
    };

    private bool Check(string label, bool current, out bool value)
    {
        value = current;
        var changed = ImGui.Checkbox(label, ref value);
        if (changed)
            plugin.RequestSave();

        return changed;
    }

    private int Number(string label, int current, int min, int max, int step, int fastStep)
    {
        var value = current;
        if (ImGui.InputInt(label, ref value, step, fastStep))
        {
            value = Math.Clamp(value, min, max);
            plugin.RequestSave();
        }

        return value;
    }

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
