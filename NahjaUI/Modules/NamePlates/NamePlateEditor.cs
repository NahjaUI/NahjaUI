using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NahjaUI.Core.Layout;
using NahjaUI.Core.Ui;

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
        var settings = plugin.Configuration.NamePlates;

        Widgets.Card("##card_np_general", 6, () =>
        {
            Widgets.SectionLabel("Nameplates");

            if (Check("Enabled", settings.Enabled, out var enabled))
            {
                settings.Enabled = enabled;
                Plugin.NamePlateGui.RequestRedraw();
            }

            if (Check("Show the game's flags and colors next to the name (test)", settings.ShowGameFlags, out var flagsShown))
                settings.ShowGameFlags = flagsShown;

            if (Check("Always show the HP bar of my target", settings.ShowBarOnTarget, out var barOnTarget))
                settings.ShowBarOnTarget = barOnTarget;

        });

        Widgets.Card("##card_np_hp", 6, () =>
        {
            Widgets.SectionLabel("HP bar");

            settings.WidthPx = Number("Width (px)", settings.WidthPx, 40, 600, 2, 10);
            settings.HeightPx = Number("Height (px)", settings.HeightPx, 4, 100, 2, 10);
            var rounding = settings.RoundingPx;
            if (ImGui.SliderInt("Corner rounding (px)", ref rounding, 0, 12))
            {
                settings.RoundingPx = rounding;
                plugin.RequestSave();
            }
        });

        Widgets.Card("##card_np_name", 17, () =>
        {
            Widgets.SectionLabel("Name");

            var anchor = settings.NameAnchor;
            if (EnumCombo("Name position", ref anchor, point => point.Label()))
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

            var nameFont = settings.NameFont;
            if (EnumCombo("Text font", ref nameFont, FontLabel))
            {
                settings.NameFont = nameFont;
                plugin.RequestSave();
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

            if (Check("Show the game's marker above the plate", settings.ShowMarkerIcon, out var showMarker))
                settings.ShowMarkerIcon = showMarker;

            settings.MarkerIconSizePx = Number("Marker size (px)", settings.MarkerIconSizePx, 12, 128, 1, 4);
            settings.MarkerOffsetX = Number("Marker offset X (px)", settings.MarkerOffsetX, -300, 300, 1, 5);
            settings.MarkerOffsetY = -Number("Marker offset Y (px)", -settings.MarkerOffsetY, -300, 300, 1, 5);

            // Which plates show a name and how it is written (full name, surname, initials...) are the game's own options.
            ImGui.TextColored(Theme.TextMuted, "When the name shows, how it is written and whether the title shows (it joins the name): set in the game.");
        });

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

            ImGui.TextColored(Theme.TextMuted, "The level is written right after the name (enemies only).");
        });

        Widgets.Card("##card_np_cast", 8, () =>
        {
            Widgets.SectionLabel("Cast frame");

            if (Check("Show the cast frame", settings.ShowCast, out var showCast))
                settings.ShowCast = showCast;

            var castHeight = settings.CastHeightPx;
            if (ImGui.SliderInt("Height under the HP bar (px)", ref castHeight, 6, 30))
            {
                settings.CastHeightPx = castHeight;
                plugin.RequestSave();
            }

            var castGap = settings.CastGapPx;
            if (ImGui.SliderInt("Space under the HP bar (px)", ref castGap, 0, 12))
            {
                settings.CastGapPx = castGap;
                plugin.RequestSave();
            }

            if (ImGui.BeginCombo("Text size (px) ##cast", $"{settings.CastTextSize} px", ImGuiComboFlags.None))
            {
                foreach (var size in OverlayFonts.AvailableSizes)
                {
                    if (ImGui.Selectable($"{size} px", settings.CastTextSize == size))
                    {
                        settings.CastTextSize = size;
                        plugin.RequestSave();
                    }
                }

                ImGui.EndCombo();
            }

            var castFont = settings.CastFont;
            if (EnumCombo("Text font ##cast", ref castFont, FontLabel))
            {
                settings.CastFont = castFont;
                plugin.RequestSave();
            }

            ImGui.TextColored(Theme.TextMuted, "Colors: the same as the target cast bar (Cast bars page). The color shows if you can interrupt it.");
        });
    }

    private static string LevelModeLabel(NamePlateLevelMode mode) => mode switch
    {
        NamePlateLevelMode.Hidden => "Do not show it",
        NamePlateLevelMode.NumberOnly => "Only the number (100)",
        NamePlateLevelMode.WithPrefix => "With the prefix (Lv 100)",
        _ => mode.ToString()
    };

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
